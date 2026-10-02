using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Backend.Api.DTOs.Agenda;
using Backend.Api.DTOs.Festa;
using Backend.Api.DTOs.Loja;
using Backend.Business.Agenda.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Models;
using Backend.Business.Festa.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Loja.Models;
using Backend.Data.Repositories;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Loja;

/// <summary>
/// O cancelamento da compra (Sprint 38) contra a API e o Postgres de verdade: a revogação, o lugar de volta e o
/// estorno numa operação, a idempotência sob clique duplo, o pedido do comprador e as portas de saída da agenda
/// e do encerramento.
/// </summary>
public sealed partial class LojaEndpointsTests
{
    /// <summary>Decisões 1, 3 e 6, P4 e P5: cancelar um de três convites, com dois cliques ao mesmo tempo.</summary>
    [Fact]
    public async Task Cancelar_um_de_tres_convites_revoga_devolve_o_lugar_e_estorna_uma_vez()
    {
        // Arrange
        await using var loja = await Montar(estoque: 5, comFesta: true);
        var gestao = Cliente(loja.Api, loja.Turma.Presidente.Cliente);
        var (compra, token) = await CompradaEPaga(loja, 3);
        var convites = await Ler<List<ConviteDaCompraDTO>>(await gestao.GetAsync($"/api/v1/loja/compras/{compra}/convites", Ct));
        var alvo = convites[1];

        // Act — dois cliques no mesmo convite
        var respostas = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(_ => Cancelar(gestao, compra, new CancelamentoRequestDTO([alvo.Id], "Desistiu da festa")))
        );

        // Assert — um cancelou, o outro achou o convite já cancelado
        respostas.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        await Problema(respostas.Single(r => r.StatusCode != HttpStatusCode.OK), HttpStatusCode.Conflict, "loja.compra_ja_cancelada");
        var feito = (await respostas.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<CompraCanceladaDTO>(Json, Ct))!;
        feito.ShouldBe(new CompraCanceladaDTO(1, 20_000));
        (await Reservados(loja)).ShouldBe(2);

        var pelaCompra = await Ler<CompraDTO>(await loja.Anonimo.GetAsync($"/api/v1/loja/compras/{token}", Ct));
        pelaCompra.Status.ShouldBe(StatusDaCompra.ADevolver);
        pelaCompra.ConvitesCancelados.ShouldBe(1);
        pelaCompra.ValorADevolverEmCentavos.ShouldBe(20_000);
        pelaCompra.Convites.Select(c => c.Codigo).ShouldBe([convites[0].Codigo, convites[2].Codigo]);

        var portaria = await Ler<ConsultaNaPortariaDTO>(await gestao.GetAsync($"/api/v1/festa/portaria/convites/{alvo.Codigo}", Ct));
        portaria.Convite.Situacao.ShouldBe(SituacaoNaPortaria.Revogado);
        portaria.Convite.MotivoDaRevogacao.ShouldBe("compra cancelada: Desistiu da festa");

        var resumo = await Ler<ResumoDaLojaDTO>(await gestao.GetAsync("/api/v1/loja/compras/resumo", Ct));
        resumo.ShouldBe(new ResumoDaLojaDTO(2, 0, 1, 40_000));

        await using var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId);
        var receitas = await contexto.OutrasReceitas.AsNoTracking().ToListAsync(Ct);
        receitas.Sum(r => r.ValorEmCentavos).ShouldBe(40_000);
        var estorno = receitas.Single(r => r.EhEstorno);
        estorno.ValorEmCentavos.ShouldBe(-20_000);
        estorno.EstornoDeId.ShouldBe(receitas.Single(r => !r.EhEstorno).Id);
        (await contexto.Eventos.CountAsync(e => e.Nome == NomesDeAuditoria.CompraCancelada && e.FormaturaId == loja.Turma.FormaturaId, Ct)).ShouldBe(
            1
        );
        (await contexto.EmailsFila.CountAsync(e => e.Assunto.StartsWith("Convite cancelado"), Ct)).ShouldBeGreaterThanOrEqualTo(1);
    }

    /// <summary>P3 e P8: convite que entrou na festa e compra ainda não paga não se cancelam.</summary>
    [Fact]
    public async Task Convite_com_entrada_e_compra_nao_paga_nao_se_cancelam()
    {
        await using var loja = await Montar(estoque: 5, comFesta: true);
        var gestao = Cliente(loja.Api, loja.Turma.Presidente.Cliente);
        var (paga, _) = await CompradaEPaga(loja, 2);
        var pendente = await Ler<CompraCriadaDTO>(await Comprar(loja, Pedido(1, Email(), AdesaoDeTeste.NovoCpf())));

        await using (var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId))
        {
            var convite = await contexto.ConvitesDoEvento.AsNoTracking().FirstAsync(c => c.CompraId == paga, Ct);
            (
                await new ConviteDoEventoRepository(contexto).RegistrarEntrada(
                    convite.Id,
                    convite.EventoId,
                    loja.Turma.Presidente.UsuarioId,
                    DateTime.UtcNow,
                    null,
                    Ct
                )
            ).ShouldBeTrue();
        }

        await Problema(await Cancelar(gestao, paga, new CancelamentoRequestDTO(null, "Festa")), HttpStatusCode.Conflict, "loja.convite_ja_validado");
        await Problema(
            await Cancelar(gestao, pendente.Compra.Id, new CancelamentoRequestDTO(null, "Festa")),
            HttpStatusCode.Conflict,
            "loja.compra_nao_paga"
        );
        await Problema(await Cancelar(gestao, paga, new CancelamentoRequestDTO(null, " ")), HttpStatusCode.BadRequest, "motivo");
        (await Reservados(loja)).ShouldBe(3);
    }

    /// <summary>P1 e decisão 5: o comprador pede pelo link, a Gestão recusa, ele pede de novo e ela aprova.</summary>
    [Fact]
    public async Task Comprador_pede_pelo_link_e_a_gestao_recusa_ou_aprova()
    {
        // Arrange
        await using var loja = await Montar(estoque: 5, comFesta: true);
        var gestao = Cliente(loja.Api, loja.Turma.Presidente.Cliente);
        var formando = await fabrica.FormandoComAdesao(loja.Turma.FormaturaId);
        var (compra, token) = await CompradaEPaga(loja, 2);
        var url = $"/api/v1/loja/compras/{token}/pedido-de-cancelamento";

        // Act — pede duas vezes
        var pedida = await Ler<CompraDTO>(
            await loja.Anonimo.PostAsJsonAsync(url, new PedidoDeCancelamentoRequestDTO(null, "Não vou poder ir"), Json, Ct)
        );
        await Ler<CompraDTO>(await loja.Anonimo.PostAsJsonAsync(url, new PedidoDeCancelamentoRequestDTO(null, null), Json, Ct));
        var fila = await Ler<List<PedidoNaGestaoDTO>>(await gestao.GetAsync("/api/v1/loja/pedidos-de-cancelamento", Ct));

        // Assert — um pedido, aberto, com os dois convites; os convites valem; formando não vê a fila
        pedida.PedidoDeCancelamento!.Status.ShouldBe(StatusDoPedidoDeCancelamento.Aberto);
        pedida.PodePedirCancelamento.ShouldBeFalse();
        pedida.Convites.Count().ShouldBe(2);
        var pedido = fila.ShouldHaveSingleItem();
        pedido.Convites.ShouldBe(2);
        pedido.Motivo.ShouldBe("Não vou poder ir");
        (await formando.Cliente.GetAsync("/api/v1/loja/pedidos-de-cancelamento", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Act — recusa, e o comprador pede um só
        (
            await gestao.PostAsJsonAsync(
                $"/api/v1/loja/pedidos-de-cancelamento/{pedido.Id}/recusa",
                new MotivoRequestDTO("Fora do prazo da turma"),
                Json,
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var recusada = await Ler<CompraDTO>(await loja.Anonimo.GetAsync($"/api/v1/loja/compras/{token}", Ct));
        var umConvite = recusada.Convites.First().Id;
        await Ler<CompraDTO>(await loja.Anonimo.PostAsJsonAsync(url, new PedidoDeCancelamentoRequestDTO([umConvite], null), Json, Ct));
        var segundo = (await Ler<List<PedidoNaGestaoDTO>>(await gestao.GetAsync("/api/v1/loja/pedidos-de-cancelamento", Ct))).Single();
        var aprovada = await Ler<CompraCanceladaDTO>(
            await gestao.PostAsync($"/api/v1/loja/pedidos-de-cancelamento/{segundo.Id}/aprovacao", null, Ct)
        );
        var repetida = await gestao.PostAsync($"/api/v1/loja/pedidos-de-cancelamento/{segundo.Id}/aprovacao", null, Ct);

        // Assert — a recusa aparece no link; a aprovação cancela o convite pedido, uma vez
        recusada.PedidoDeCancelamento!.Status.ShouldBe(StatusDoPedidoDeCancelamento.Recusado);
        recusada.PedidoDeCancelamento.MotivoDaResposta.ShouldBe("Fora do prazo da turma");
        recusada.PodePedirCancelamento.ShouldBeTrue();
        aprovada.ConvitesCancelados.ShouldBe(1);
        await Problema(repetida, HttpStatusCode.Conflict, "loja.pedido_ja_respondido");
        var depois = await Ler<CompraDTO>(await loja.Anonimo.GetAsync($"/api/v1/loja/compras/{token}", Ct));
        depois.PedidoDeCancelamento!.Status.ShouldBe(StatusDoPedidoDeCancelamento.Aprovado);
        depois.Convites.ShouldNotContain(c => c.Id == umConvite);
        await using var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId);
        (await contexto.EmailsFila.CountAsync(e => e.Assunto.StartsWith("Pedido de cancelamento"), Ct)).ShouldBeGreaterThanOrEqualTo(2);
        (await contexto.ComprasDeConvite.AsNoTracking().SingleAsync(c => c.Id == compra, Ct)).ConvitesCancelados.ShouldBe(1);
    }

    /// <summary>P10, P11 e decisão 2: a agenda que só cancela sem vendas, e a devolução que tira da lista.</summary>
    [Fact]
    public async Task Sem_vendas_a_agenda_cancela_a_festa_e_a_devolucao_tira_da_lista()
    {
        // Arrange
        await using var loja = await Montar(estoque: 5, comFesta: true);
        var gestao = Cliente(loja.Api, loja.Turma.Presidente.Cliente);
        var (primeira, _) = await CompradaEPaga(loja, 2);
        var (segunda, _) = await CompradaEPaga(loja, 1);
        (
            await gestao.PostAsJsonAsync(
                "/api/v1/festa/convites/cortesias",
                new CortesiaRequestDTO("Prof. Carlos", null, null, null, "Paraninfo"),
                Json,
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.OK);
        Guid festa;
        await using (var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId))
            festa = (await contexto.EventosDaTurma.AsNoTracking().SingleAsync(e => e.Tipo == TipoDeEvento.Festa, Ct)).Id;
        var cancelarNaAgenda = new EventoRequestDTO(
            "Festa de formatura",
            TipoDeEvento.Festa,
            SituacaoDoEvento.Cancelado,
            DataUtils.Hoje().AddMonths(3),
            new TimeOnly(22, 0),
            "Espaço Vitrália",
            null
        );

        // Act + Assert — com vendas de pé, a agenda não cancela nem exclui a festa
        await Problema(
            await gestao.PutAsJsonAsync($"/api/v1/agenda/{festa}", cancelarNaAgenda, Json, Ct),
            HttpStatusCode.Conflict,
            "agenda.evento_com_vendas"
        );
        await Problema(await gestao.DeleteAsync($"/api/v1/agenda/{festa}", Ct), HttpStatusCode.Conflict, "compras_da_loja");

        // Act — sem ação em massa, cada compra se cancela pela porta da Gestão; só então a agenda cancela a festa
        (await Cancelar(gestao, primeira, new CancelamentoRequestDTO(null, "Salão fechou"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Cancelar(gestao, segunda, new CancelamentoRequestDTO(null, "Salão fechou"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await gestao.PutAsJsonAsync($"/api/v1/agenda/{festa}", cancelarNaAgenda, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert — nenhum convite vale; a loja fecha; tudo está na lista a devolver
        await using (var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId))
        {
            (await contexto.ConvitesDoEvento.CountAsync(c => c.RevogadoEm == null, Ct)).ShouldBe(0);
            (await contexto.ConvitesDoEvento.SingleAsync(c => c.CompraId == null, Ct)).MotivoDaRevogacao.ShouldBe("evento cancelado");
        }
        (await loja.Anonimo.GetAsync($"/api/v1/loja/{loja.Turma.FormaturaId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Ler<ResumoDaLojaDTO>(await gestao.GetAsync("/api/v1/loja/compras/resumo", Ct))).ShouldBe(new ResumoDaLojaDTO(0, 0, 2, 0));

        // Act + Assert — encerrar com dinheiro a devolver é recusado, com a contagem
        var encerrar = await gestao.PostAsync("/api/v1/formaturas/atual/encerrar", null, Ct);
        await Problema(encerrar, HttpStatusCode.Conflict, "formatura.pendencias_em_aberto");
        (await encerrar.Content.ReadAsStringAsync(Ct)).ShouldContain("\"compras_a_devolver\":2");

        // Act + Assert — devolver exige comprovante, e tira a compra da lista
        var devolucao = $"/api/v1/loja/compras/{primeira}/devolucao";
        await Problema(
            await gestao.PostAsync(devolucao, new MultipartFormDataContent { { new StringContent("sem arquivo"), "observacao" } }, Ct),
            HttpStatusCode.BadRequest,
            "loja.comprovante_obrigatorio"
        );
        (await gestao.PostAsync(devolucao, Comprovante(), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await Problema(await gestao.PostAsync(devolucao, Comprovante(), Ct), HttpStatusCode.Conflict, "loja.compra_nao_a_devolver");
        (await Ler<ResumoDaLojaDTO>(await gestao.GetAsync("/api/v1/loja/compras/resumo", Ct))).ComprasADevolver.ShouldBe(1);
        await using (var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId))
        {
            var devolvida = await contexto.ComprasDeConvite.AsNoTracking().SingleAsync(c => c.Id == primeira, Ct);
            devolvida.Status.ShouldBe(StatusDaCompra.Devolvida);
            devolvida.ComprovanteDaDevolucaoId.ShouldNotBeNull();
            (await contexto.OutrasReceitas.SumAsync(r => r.ValorEmCentavos, Ct)).ShouldBe(0);
        }
    }

    /// <summary>Compra, paga pelo aviso do Mercado Pago, com os convites emitidos.</summary>
    private async Task<(Guid Compra, string Token)> CompradaEPaga(Loja loja, int quantidade)
    {
        var criada = await Ler<CompraCriadaDTO>(await Comprar(loja, Pedido(quantidade, Email(), AdesaoDeTeste.NovoCpf())));

        string idExterno;
        await using (var contexto = fabrica.ContextoDe(loja.Turma.FormaturaId))
            idExterno = (await contexto.CobrancasBancarias.SingleAsync(c => c.CompraId == criada.Compra.Id, Ct)).IdExterno!;
        loja.Falso.Pagar(idExterno);
        (await Avisar(loja, idExterno)).StatusCode.ShouldBe(HttpStatusCode.OK);

        return (criada.Compra.Id, criada.Token);
    }

    private static Task<HttpResponseMessage> Cancelar(HttpClient gestao, Guid compra, CancelamentoRequestDTO dados) =>
        gestao.PostAsJsonAsync($"/api/v1/loja/compras/{compra}/cancelamento", dados, Json, Ct);

    private static MultipartFormDataContent Comprovante()
    {
        var arquivo = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4 comprovante do PIX de volta"));
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        return new MultipartFormDataContent { { arquivo, "comprovante", "comprovante.pdf" } };
    }
}
