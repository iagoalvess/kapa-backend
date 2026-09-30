using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Pagamentos;
using Backend.Api.DTOs.Recebimentos;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.MercadoPago.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Backend.IntegrationTests.Pagamentos;

/// <summary>
/// A Sprint 42 de ponta a ponta: cada falha tratada (F1, F2, F3, F6, F7, F9, F10) contra a API e o Postgres de verdade,
/// com o Mercado Pago falso no lugar da rede onde ele entra.
/// </summary>
/// <remarks>
/// O Kapa registra, a comissão resolve: nenhum teste espera o Kapa mover dinheiro — só que o registro nunca fique preso
/// e que a lista "a devolver" mostre o que a comissão tem de resolver.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class DevolucoesEndpointsTests(ApiFactory fabrica)
{
    private const string Conta = "/api/v1/recebimentos/conta/mercado-pago";

    private const string Aviso = "/api/v1/webhooks/cobranca/mercadopago";

    private const string ADevolver = "/api/v1/valores-a-devolver";

    /// <summary>A mensalidade da <see cref="CobrancaDeTeste.TurmaComPlano"/>: R$ 2.400 em 12×.</summary>
    private const long Mensalidade = 20_000;

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// F1: o crédito do pedido cancelado deixa de ser parcela negativa aberta para sempre. Ele vai para a lista, trava o
    /// encerramento como pendência própria, e a devolução com comprovante o fecha — a saída entra no caixa e a turma
    /// encerra.
    /// </summary>
    [Fact]
    public async Task Credito_de_pedido_cancelado_vai_para_a_lista_e_devolvido_libera_o_encerramento()
    {
        // Arrange — o formando pediu e pagou um convite; o resto da turma está em dia
        var turma = await fabrica.TurmaComPlano();
        var presidente = turma.Presidente.Cliente;
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId);
        await CancelarMensalidades(turma.FormaturaId);
        var item = await Ler<ItemDeCobrancaDTO>(
            await presidente.PostAsJsonAsync(
                "/api/v1/cobrancas/opcionais",
                new OpcionalRequestDTO(
                    TipoDeCobranca.ConviteExtra,
                    "Convite extra",
                    18_000,
                    1,
                    10,
                    DataUtils.Hoje().AddMonths(1),
                    null,
                    null,
                    null,
                    null,
                    null
                ),
                Json,
                Ct
            )
        );
        var pedido = await Ler<PedidoDTO>(await formando.Cliente.PostAsJsonAsync("/api/v1/pedidos", new PedidoRequestDTO(item.Id, 1, 1), Json, Ct));
        var parcelaDoPedido = await ParcelaDoItem(turma.FormaturaId, item.Id);
        (
            await presidente.PostAsync($"/api/v1/parcelas/{parcelaDoPedido}/baixa-manual", Baixa(FormaDePagamento.Pix, 18_000), Ct)
        ).EnsureSuccessStatusCode();

        // Act — a tesouraria cancela o pedido devolvendo tudo
        (
            await presidente.PostAsJsonAsync($"/api/v1/pedidos/{pedido.Id}/cancelar", new CancelarPedidoRequestDTO(18_000), Json, Ct)
        ).EnsureSuccessStatusCode();
        var travado = await presidente.PostAsync("/api/v1/formaturas/atual/encerrar", null, Ct);

        // Assert — nenhuma parcela negativa, e o crédito é a pendência que falta
        await using (var contexto = fabrica.ContextoDe(turma.FormaturaId))
            (await contexto.Parcelas.AnyAsync(p => p.ValorOriginalEmCentavos < 0, Ct)).ShouldBeFalse();
        await Problema(travado, HttpStatusCode.Conflict, "formatura.pendencias_em_aberto");
        var corpo = await travado.Content.ReadAsStringAsync(Ct);
        corpo.ShouldContain("\"parcelas_em_aberto\":0");
        corpo.ShouldContain("\"valores_a_devolver\":1");
        var credito = (await Ler<PaginaDTO<ValorADevolverDTO>>(await presidente.GetAsync(ADevolver, Ct))).Itens.ShouldHaveSingleItem();
        credito.Origem.ShouldBe(OrigemDoValorADevolver.CreditoDePedido);
        credito.ValorEmCentavos.ShouldBe(18_000);
        credito.Tipo.ShouldBe(TipoDeCobranca.ConviteExtra);

        // Act + Assert — crédito não fecha sem comprovante, nem pela porta do pago sem parcela
        await Problema(
            await presidente.PostAsync(
                $"{ADevolver}/{credito.Id}/devolucao",
                new MultipartFormDataContent { { new StringContent("x"), "observacao" } },
                Ct
            ),
            HttpStatusCode.BadRequest,
            "pagamento.comprovante_obrigatorio"
        );
        await Problema(
            await presidente.PostAsJsonAsync($"{ADevolver}/{credito.Id}/fechar", new FecharValorADevolverRequestDTO("compensado"), Json, Ct),
            HttpStatusCode.Conflict,
            "pagamento.devolucao_exige_comprovante"
        );

        // Act — a comissão fez o PIX de volta e anexa o comprovante
        var devolvido = await Ler<ValorADevolverDTO>(await presidente.PostAsync($"{ADevolver}/{credito.Id}/devolucao", Comprovante(), Ct));

        // Assert — sai da lista, a saída é uma despesa paga com o comprovante, e a turma encerra
        devolvido.Status.ShouldBe(StatusDoValorADevolver.Devolvido);
        devolvido.TemComprovante.ShouldBeTrue();
        await using (var contexto = fabrica.ContextoDe(turma.FormaturaId))
        {
            var saida = await contexto.Despesas.SingleAsync(Ct);
            saida.Status.ShouldBe(StatusDaDespesa.Paga);
            saida.ValorEmCentavos.ShouldBe(18_000);
            saida.ComprovanteArquivoId.ShouldNotBeNull();
        }
        (await Ler<PaginaDTO<ValorADevolverDTO>>(await presidente.GetAsync(ADevolver, Ct))).Total.ShouldBe(0);
        await Problema(
            await presidente.PostAsync($"{ADevolver}/{credito.Id}/devolucao", Comprovante(), Ct),
            HttpStatusCode.Conflict,
            "pagamento.valor_nao_a_devolver"
        );
        (await presidente.PostAsync("/api/v1/formaturas/atual/assinatura/cancelar", null, Ct)).EnsureSuccessStatusCode();
        var encerrada = await presidente.PostAsync("/api/v1/formaturas/atual/encerrar", null, Ct);
        encerrada.StatusCode.ShouldBe(HttpStatusCode.NoContent, await encerrada.Content.ReadAsStringAsync(Ct));
    }

    /// <summary>
    /// Decisão 8 e F2: a tesouraria cancela uma parcela avulsa com justificativa; o que já tinha entrado nela vai para a
    /// lista, e o estorno dessa baixa — que antes era recusado em parcela cancelada — passa e tira o valor da lista.
    /// </summary>
    [Fact]
    public async Task Parcela_cancelada_com_pagamento_parcial_manda_o_parcial_para_a_lista()
    {
        // Arrange — R$ 100 de R$ 200 pagos na primeira parcela, a segunda quitada
        var turma = await fabrica.TurmaComPlano();
        var presidente = turma.Presidente.Cliente;
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId);
        var comissao = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Comissao, Ct);
        var (parcial, paga) = await DuasParcelas(turma.FormaturaId, formando);
        (await presidente.PostAsync($"/api/v1/parcelas/{parcial}/baixa-manual", Baixa(FormaDePagamento.Pix, 10_000), Ct)).EnsureSuccessStatusCode();
        (await presidente.PostAsync($"/api/v1/parcelas/{paga}/baixa-manual", Baixa(FormaDePagamento.Pix, Mensalidade), Ct)).EnsureSuccessStatusCode();

        // Act + Assert — só a tesouraria, só com justificativa, e paga não cancela
        (await Cancelar(comissao.Cliente, parcial, "Desistiu")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await Problema(await Cancelar(presidente, parcial, " "), HttpStatusCode.BadRequest, "justificativa");
        await Problema(await Cancelar(presidente, paga, "Engano"), HttpStatusCode.Conflict, "pagamento.parcela_paga");

        // Act — a tesouraria cancela a parcial
        var cancelada = await Ler<ParcelaDTO>(await Cancelar(presidente, parcial, "Acordo com a comissão"));

        // Assert — cancelada, o parcial na lista, e a auditoria com a justificativa
        cancelada.Status.ShouldBe(StatusDaParcela.Cancelada);
        var parcialNaLista = (await Ler<PaginaDTO<ValorADevolverDTO>>(await presidente.GetAsync(ADevolver, Ct))).Itens.ShouldHaveSingleItem();
        parcialNaLista.Origem.ShouldBe(OrigemDoValorADevolver.ParcelaCancelada);
        parcialNaLista.ValorEmCentavos.ShouldBe(10_000);
        parcialNaLista.NumeroDaParcela.ShouldNotBeNull();
        await using (var contexto = fabrica.ContextoDe(null))
        {
            var evento = await contexto.Eventos.SingleAsync(
                e => e.Nome == NomesDeAuditoria.ParcelaCancelada && e.UsuarioId == turma.Presidente.UsuarioId,
                Ct
            );
            evento.Dados!.ShouldContain("Acordo com a comissão");
            evento.FormaturaId.ShouldBe(turma.FormaturaId);
        }
        await Problema(await Cancelar(presidente, parcial, "De novo"), HttpStatusCode.Conflict, "pagamento.parcela_nao_aberta");

        // Act — o presidente estorna a baixa da parcela cancelada: o dinheiro não tinha entrado
        var estornada = await Ler<ParcelaDTO>(
            await presidente.PostAsJsonAsync(
                $"/api/v1/parcelas/{parcial}/estornar-baixa",
                new EstornarBaixaRequestDTO("Baixa lançada errado"),
                Json,
                Ct
            )
        );

        // Assert — continua cancelada, e não há mais nada a devolver
        estornada.Status.ShouldBe(StatusDaParcela.Cancelada);
        estornada.ValorPagoEmCentavos.ShouldBeNull();
        (await Ler<PaginaDTO<ValorADevolverDTO>>(await presidente.GetAsync(ADevolver, Ct))).Total.ShouldBe(0);
        (await Ler<PaginaDTO<ValorADevolverDTO>>(await presidente.GetAsync($"{ADevolver}?resolvidos=true", Ct)))
            .Itens.ShouldHaveSingleItem()
            .Status.ShouldBe(StatusDoValorADevolver.Fechado);
    }

    /// <summary>
    /// F2 pelo Mercado Pago: a parcela paga em parte pelo PIX dele e depois cancelada. A devolução no painel deixava a
    /// transação inteira cair; agora estorna a baixa, a parcela segue cancelada e o valor sai da lista sozinho.
    /// </summary>
    [Fact]
    public async Task Devolucao_do_mercado_pago_em_parcela_cancelada_estorna_e_tira_da_lista()
    {
        // Arrange
        var falso = new MercadoPagoFalso();
        await using var api = falso.Na(fabrica);
        var (turma, presidente, aluno) = await Conectada(api);
        var (parcela, pedido) = await PixDaProxima(aluno, falso);
        falso.Pagar(pedido, 10_000);
        (await Avisar(Cliente(api, null), pedido)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Cancelar(presidente, parcela, "Formando saiu da festa")).EnsureSuccessStatusCode();

        // Act
        falso.Devolver(pedido, SituacaoDoPedido.Devolvido);
        var entregue = await Avisar(Cliente(api, null), pedido);

        // Assert
        entregue.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var contexto = fabrica.ContextoDe(turma);
        var lida = await contexto.Parcelas.SingleAsync(p => p.Id == parcela, Ct);
        lida.Status.ShouldBe(StatusDaParcela.Cancelada);
        lida.ValorPagoEmCentavos.ShouldBeNull();
        (await contexto.Recebimentos.SingleAsync(r => r.ParcelaId == parcela, Ct)).EstornadoEm.ShouldNotBeNull();
        var valor = await contexto.ValoresADevolver.SingleAsync(Ct);
        valor.Status.ShouldBe(StatusDoValorADevolver.Fechado);
        (await contexto.CobrancasBancarias.SingleAsync(Ct)).Status.ShouldBe(StatusDaCobrancaBancaria.Estornada);
    }

    /// <summary>
    /// F3: o Mercado Pago baixa parte da parcela e a tesouraria baixa o resto por PIX manual, depois. A devolução do
    /// Mercado Pago estorna a baixa dele — antes, pegava a mais recente com a mesma forma, que era a manual.
    /// </summary>
    [Fact]
    public async Task Devolucao_no_mercado_pago_estorna_a_baixa_daquela_cobranca_e_nao_a_manual_feita_depois()
    {
        // Arrange
        var falso = new MercadoPagoFalso();
        await using var api = falso.Na(fabrica);
        var (turma, presidente, aluno) = await Conectada(api);
        var (parcela, pedido) = await PixDaProxima(aluno, falso);
        falso.Pagar(pedido, 10_000);
        (await Avisar(Cliente(api, null), pedido)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await presidente.PostAsync($"/api/v1/parcelas/{parcela}/baixa-manual", Baixa(FormaDePagamento.Pix, 10_000), Ct)).EnsureSuccessStatusCode();

        // Act
        falso.Devolver(pedido, SituacaoDoPedido.Devolvido);
        (await Avisar(Cliente(api, null), pedido)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert — a do Mercado Pago estornada, a manual de pé, e a parcela aberta pelo que o MP levou
        await using var contexto = fabrica.ContextoDe(turma);
        var cobranca = await contexto.CobrancasBancarias.SingleAsync(Ct);
        var baixas = await contexto.Recebimentos.Where(r => r.ParcelaId == parcela).ToListAsync(Ct);
        baixas.Count.ShouldBe(2);
        baixas.Single(r => r.CobrancaId == cobranca.Id).EstornadoEm.ShouldNotBeNull();
        baixas.Single(r => r.CobrancaId == null).EstornadoEm.ShouldBeNull();
        var lida = await contexto.Parcelas.SingleAsync(p => p.Id == parcela, Ct);
        lida.Status.ShouldBe(StatusDaParcela.Aberta);
        lida.ValorPagoEmCentavos.ShouldBe(10_000);
        cobranca.Status.ShouldBe(StatusDaCobrancaBancaria.Estornada);
    }

    /// <summary>
    /// F6 e decisão 4: o presidente estorna à mão a baixa que o cartão fez. A cobrança passa a estornada, o acréscimo do
    /// cartão sai do caixa, e o aviso da devolução que chega depois não desfaz nada de novo.
    /// </summary>
    [Fact]
    public async Task Presidente_estorna_a_baixa_do_mercado_pago_e_o_aviso_atrasado_nao_desfaz_de_novo()
    {
        // Arrange — a parcela paga no cartão com a taxa repassada
        var falso = new MercadoPagoFalso();
        await using var api = falso.Na(fabrica);
        var (turma, presidente, aluno) = await Conectada(api);
        (await presidente.PutAsJsonAsync($"{Conta}/cartao", new ConfiguracaoDoCartaoRequestDTO(true, 500), Json, Ct)).EnsureSuccessStatusCode();
        var parcela = (await Ler<ExtratoDTO>(await aluno.GetAsync("/api/v1/extrato/eu", Ct))).Proxima!.Id;
        var cobranca = await Ler<CobrancaDaParcelaDTO>(await aluno.GetAsync($"/api/v1/parcelas/{parcela}/cobranca", Ct));
        var cartao = cobranca.PeloMercadoPago.Single(m => m.Meio == MeioDePagamento.Cartao).Cartao!;
        await Ler<PagamentoNoCartaoDTO>(
            await aluno.PostAsJsonAsync(
                "/api/v1/parcelas/cartao",
                new PagamentoNoCartaoRequestDTO([parcela], "tok-visa", "visa", 1, cartao.ValorEmCentavos),
                Json,
                Ct
            )
        );
        (await Ler<ParcelaDTO>(await presidente.GetAsync($"/api/v1/parcelas/{parcela}", Ct))).PeloMercadoPago.ShouldBeTrue();

        // Act — a comissão devolveu no painel e registra no Kapa
        var estornada = await Ler<ParcelaDTO>(
            await presidente.PostAsJsonAsync(
                $"/api/v1/parcelas/{parcela}/estornar-baixa",
                new EstornarBaixaRequestDTO("Devolvido no painel do MP"),
                Json,
                Ct
            )
        );

        // Assert
        estornada.Status.ShouldNotBe(StatusDaParcela.Paga);
        estornada.PeloMercadoPago.ShouldBeFalse();
        string idDoPedido;
        await using (var contexto = fabrica.ContextoDe(turma))
        {
            var lida = await contexto.CobrancasBancarias.SingleAsync(c => c.Meio == MeioDePagamento.Cartao, Ct);
            lida.Status.ShouldBe(StatusDaCobrancaBancaria.Estornada);
            idDoPedido = lida.IdExterno!;
            (await contexto.OutrasReceitas.SumAsync(r => r.ValorEmCentavos, Ct)).ShouldBe(0);
        }

        // Act — o aviso da devolução chega depois
        falso.Devolver(idDoPedido, SituacaoDoPedido.Devolvido);
        (await Avisar(Cliente(api, null), idDoPedido)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert — nada desfeito de novo
        await using var depois = fabrica.ContextoDe(turma);
        (await depois.Recebimentos.CountAsync(r => r.ParcelaId == parcela, Ct)).ShouldBe(1);
        (await depois.OutrasReceitas.CountAsync(Ct)).ShouldBe(2);
    }

    /// <summary>
    /// F7 e decisão 9: o PIX da parcela cancelada é pago. O dinheiro vira pendência da tesouraria na lista; não se
    /// devolve por PIX (está no Mercado Pago), e a comissão fecha dizendo o que fez.
    /// </summary>
    [Fact]
    public async Task Pago_sem_parcela_aparece_para_a_tesouraria_ate_ela_fechar_o_aviso()
    {
        // Arrange — o PIX gerado, e a parcela cancelada antes do pagamento
        var falso = new MercadoPagoFalso();
        await using var api = falso.Na(fabrica);
        var (_, presidente, aluno) = await Conectada(api);
        var (parcela, pedido) = await PixDaProxima(aluno, falso);
        (await Cancelar(presidente, parcela, "Item encerrado com a turma")).EnsureSuccessStatusCode();

        // Act
        falso.Pagar(pedido);
        (await Avisar(Cliente(api, null), pedido)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert — está na lista, como pago sem parcela
        var aviso = (await Ler<PaginaDTO<ValorADevolverDTO>>(await presidente.GetAsync(ADevolver, Ct))).Itens.ShouldHaveSingleItem();
        aviso.Origem.ShouldBe(OrigemDoValorADevolver.PagoSemParcela);
        aviso.ValorEmCentavos.ShouldBe(Mensalidade);

        // Act + Assert — não se devolve por PIX, e fechar pede o que foi feito
        await Problema(
            await presidente.PostAsync($"{ADevolver}/{aviso.Id}/devolucao", Comprovante(), Ct),
            HttpStatusCode.Conflict,
            "pagamento.pago_sem_parcela_nao_devolve"
        );
        await Problema(
            await presidente.PostAsJsonAsync($"{ADevolver}/{aviso.Id}/fechar", new FecharValorADevolverRequestDTO(""), Json, Ct),
            HttpStatusCode.BadRequest,
            "observacao"
        );
        var fechado = await Ler<ValorADevolverDTO>(
            await presidente.PostAsJsonAsync(
                $"{ADevolver}/{aviso.Id}/fechar",
                new FecharValorADevolverRequestDTO("Devolvido no painel do MP"),
                Json,
                Ct
            )
        );

        fechado.Status.ShouldBe(StatusDoValorADevolver.Fechado);
        fechado.Observacao.ShouldBe("Devolvido no painel do MP");
        (await Ler<PaginaDTO<ValorADevolverDTO>>(await presidente.GetAsync(ADevolver, Ct))).Total.ShouldBe(0);
    }

    /// <summary>F9 e decisão 7: a suspensa há um ano encerra mesmo com parcela aberta, e a auditoria diz o que ficou.</summary>
    [Fact]
    public async Task Suspensa_abandonada_encerra_e_registra_as_pendencias_na_auditoria()
    {
        // Arrange — a turma com as doze mensalidades de um formando em aberto, suspensa há mais de um ano
        var turma = await fabrica.TurmaComPlano();
        await fabrica.FormandoComAdesao(turma.FormaturaId);
        await using (var contexto = fabrica.ContextoDe(null))
            await contexto
                .Formaturas.Where(f => f.Id == turma.FormaturaId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(f => f.Status, StatusDaFormatura.Suspensa).SetProperty(f => f.StatusDesde, DateTime.UtcNow.AddDays(-400)),
                    Ct
                );

        // Act
        bool encerrada;
        using (var escopo = fabrica.EscopoDoWorker(turma.FormaturaId))
            encerrada = await escopo.ServiceProvider.GetRequiredService<IRetencaoDeFormaturasService>().EncerrarAbandonada(turma.FormaturaId, Ct);

        // Assert
        encerrada.ShouldBeTrue();
        await using var depois = fabrica.ContextoDe(null);
        (await depois.Formaturas.SingleAsync(f => f.Id == turma.FormaturaId, Ct)).Status.ShouldBe(StatusDaFormatura.Encerrada);
        var evento = await depois.Eventos.SingleAsync(e => e.Nome == NomesDeAuditoria.EncerradaPorAbandono && e.FormaturaId == turma.FormaturaId, Ct);
        evento.UsuarioId.ShouldBeNull();
        evento.Dados!.ShouldContain("12 parcelas em aberto");
    }

    /// <summary>F10: a forma cartão é só da baixa automática; à mão ela é recusada.</summary>
    [Fact]
    public async Task Baixa_manual_no_cartao_e_recusada()
    {
        var turma = await fabrica.TurmaComPlano();
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId);
        var (parcela, _) = await DuasParcelas(turma.FormaturaId, formando);

        var resposta = await turma.Presidente.Cliente.PostAsync(
            $"/api/v1/parcelas/{parcela}/baixa-manual",
            Baixa(FormaDePagamento.Cartao, Mensalidade),
            Ct
        );

        await Problema(resposta, HttpStatusCode.BadRequest, "forma");
        (await ParcelaNoBanco(turma.FormaturaId, parcela)).Status.ShouldBe(StatusDaParcela.Aberta);
    }

    /// <summary>
    /// A lista é da tesouraria: a comissão e o formando não a leem nem mexem nela, e a política vale para cada rota.
    /// </summary>
    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK, HttpStatusCode.NotFound)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK, HttpStatusCode.NotFound)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task A_lista_a_devolver_e_da_tesouraria(string papel, HttpStatusCode leitura, HttpStatusCode escrita)
    {
        var turma = await fabrica.TurmaComPlano();
        var membro = await fabrica.NovoMembro(turma.FormaturaId, papel, Ct);

        (await membro.Cliente.GetAsync(ADevolver, Ct)).StatusCode.ShouldBe(leitura);
        (
            await membro.Cliente.PostAsJsonAsync($"{ADevolver}/{Guid.CreateVersion7()}/fechar", new FecharValorADevolverRequestDTO("feito"), Json, Ct)
        ).StatusCode.ShouldBe(escrita);
    }

    // ---- Montagem ----

    /// <summary>Cancela no banco as mensalidades da turma — o F1 quer só o pedido como pendência.</summary>
    private async Task CancelarMensalidades(Guid formaturaId)
    {
        await using var contexto = fabrica.ContextoDe(formaturaId);

        await contexto.Parcelas.ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, StatusDaParcela.Cancelada), Ct);
    }

    private async Task<Guid> ParcelaDoItem(Guid formaturaId, Guid itemId)
    {
        await using var contexto = fabrica.ContextoDe(formaturaId);

        return await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == itemId).Select(p => p.Id).SingleAsync(Ct);
    }

    /// <summary>As duas primeiras mensalidades do formando, por vencimento.</summary>
    private async Task<(Guid Primeira, Guid Segunda)> DuasParcelas(Guid formaturaId, MembroDeTeste formando)
    {
        await using var contexto = fabrica.ContextoDe(formaturaId);
        var vinculoId = await contexto.Vinculos.Where(v => v.UsuarioId == formando.UsuarioId).Select(v => v.Id).SingleAsync(Ct);
        var parcelas = await contexto
            .Parcelas.Where(p => p.VinculoId == vinculoId)
            .OrderBy(p => p.Vencimento)
            .Select(p => p.Id)
            .Take(2)
            .ToListAsync(Ct);

        return (parcelas[0], parcelas[1]);
    }

    private async Task<Parcela> ParcelaNoBanco(Guid formaturaId, Guid parcelaId)
    {
        await using var contexto = fabrica.ContextoDe(formaturaId);

        return await contexto.Parcelas.AsNoTracking().SingleAsync(p => p.Id == parcelaId, Ct);
    }

    /// <summary>Uma turma com plano, Mercado Pago conectado e cobrando sozinho, e um formando com adesão.</summary>
    private async Task<(Guid Turma, HttpClient Presidente, HttpClient Aluno)> Conectada(WebApplicationFactory<Program> api)
    {
        var turma = await fabrica.TurmaComPlano();
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId);
        var presidente = Cliente(api, turma.Presidente.Cliente);
        var anonimo = Cliente(api, null);

        await presidente.CadastrarChavePix(Ct);
        var autorizacao = await Ler<AutorizacaoDoProvedorDTO>(await presidente.PostAsync($"{Conta}/autorizacao", null, Ct));
        var state = HttpUtility.ParseQueryString(new Uri(autorizacao.Url).Query)["state"];
        (await anonimo.GetAsync($"/api/v1/mercado-pago/retorno?code=codigo&state={Uri.EscapeDataString(state!)}", Ct))
            .Headers.Location!.ToString()
            .ShouldContain("mercado_pago=conectado");
        (await presidente.PutAsJsonAsync($"{Conta}/cobranca", new ModoDeCobrancaRequestDTO(true), Json, Ct)).EnsureSuccessStatusCode();

        return (turma.FormaturaId, presidente, Cliente(api, formando.Cliente));
    }

    /// <summary>O PIX do Mercado Pago da próxima parcela do formando: a parcela e o pedido emitido.</summary>
    private static async Task<(Guid Parcela, string Pedido)> PixDaProxima(HttpClient aluno, MercadoPagoFalso falso)
    {
        var parcela = (await Ler<ExtratoDTO>(await aluno.GetAsync("/api/v1/extrato/eu", Ct))).Proxima!.Id;
        await Ler<CobrancaDaParcelaDTO>(await aluno.GetAsync($"/api/v1/parcelas/{parcela}/cobranca", Ct));

        return (parcela, falso.Pedidos.ShouldHaveSingleItem().Key);
    }

    private static Task<HttpResponseMessage> Cancelar(HttpClient cliente, Guid parcelaId, string justificativa) =>
        cliente.PostAsJsonAsync($"/api/v1/parcelas/{parcelaId}/cancelar", new CancelarParcelaRequestDTO(justificativa), Json, Ct);

    private static MultipartFormDataContent Baixa(FormaDePagamento forma, long valor) =>
        new()
        {
            { new StringContent(forma.ToString()), "forma" },
            { new StringContent(DataUtils.Hoje().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "pagoEm" },
            { new StringContent(valor.ToString(CultureInfo.InvariantCulture)), "valorEmCentavos" },
        };

    private static MultipartFormDataContent Comprovante() =>
        new() { { new ByteArrayContent("%PDF-1.4 comprovante do PIX de volta"u8.ToArray()), "comprovante", "comprovante.pdf" } };

    private static Task<HttpResponseMessage> Avisar(HttpClient anonimo, string idDoPedido)
    {
        var aviso = new HttpRequestMessage(HttpMethod.Post, $"{Aviso}?data.id={idDoPedido.ToLowerInvariant()}&type=order");
        aviso.Headers.Add("x-signature", MercadoPagoFalso.AssinaturaValida);
        aviso.Headers.Add("x-request-id", "req-1");

        return anonimo.SendAsync(aviso, Ct);
    }

    private static HttpClient Cliente(WebApplicationFactory<Program> api, HttpClient? sessao)
    {
        var cliente = api.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false }
        );
        cliente.DefaultRequestHeaders.Authorization = sessao?.DefaultRequestHeaders.Authorization;

        return cliente;
    }

    private static async Task Problema(HttpResponseMessage resposta, HttpStatusCode status, string trecho)
    {
        var corpo = await resposta.Content.ReadAsStringAsync(Ct);
        resposta.StatusCode.ShouldBe(status, corpo);
        corpo.ShouldContain(trecho);
    }

    private static async Task<T> Ler<T>(HttpResponseMessage resposta)
    {
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync(Ct));

        return (await resposta.Content.ReadFromJsonAsync<T>(Json, Ct))!;
    }
}
