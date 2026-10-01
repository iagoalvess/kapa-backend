using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Festa;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Festa.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using static Backend.IntegrationTests.Infra.AdesaoDeTeste;

namespace Backend.IntegrationTests.Cobrancas;

/// <summary>
/// Os opcionais e o pedido contra a API e o Postgres de verdade.
/// </summary>
/// <remarks>
/// É aqui que a sprint se prova: a garantia de não vender a mais não mora no service, e sim numa
/// trava de linha e em duas <c>CHECK</c>. Substituir o banco por um dublê testaria exatamente o que
/// não é a regra.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class PedidoEndpointsTests(ApiFactory fabrica)
{
    private const string Opcionais = "/api/v1/cobrancas/opcionais";

    private const string Pedidos = "/api/v1/pedidos";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>"Convite extra, R$ 180, em 2×, todo dia 10" — o exemplo da decisão 2.</summary>
    private static OpcionalRequestDTO Convite(
        int? estoque = null,
        int? limite = null,
        DateOnly? abertura = null,
        DateOnly? prazo = null,
        Guid? itemDaFestaId = null,
        long preco = 18_000
    ) =>
        new(
            TipoDeCobranca.ConviteExtra,
            "Convite extra",
            preco,
            2,
            10,
            DataUtils.Hoje().AddMonths(1),
            limite,
            prazo,
            estoque,
            abertura is { } dia ? DataUtils.InicioDoDiaEmUtc(dia) : null,
            itemDaFestaId
        );

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Cadastrar_opcionais_segue_a_politica_de_tesouraria(string papel, HttpStatusCode esperado)
    {
        var turma = await TurmaComPlano();
        var membro = await fabrica.NovoMembro(turma.FormaturaId, papel, Ct);

        var criacao = await membro.Cliente.PostAsJsonAsync(Opcionais, Convite(), Json, Ct);

        criacao.StatusCode.ShouldBe(esperado);
    }

    /// <summary>Todo membro lê a vitrine — é ela que abre em "Minhas parcelas".</summary>
    [Fact]
    public async Task Vitrine_e_de_todo_membro_e_traz_so_o_que_esta_a_venda()
    {
        var turma = await TurmaComPlano();
        var aberto = await CriarItem(turma.Presidente.Cliente, Convite());
        var comPrazoVencido = await CriarItem(turma.Presidente.Cliente, Convite(prazo: DataUtils.Hoje().AddDays(-1)));
        var encerrado = await CriarItem(turma.Presidente.Cliente, Convite());
        (await turma.Presidente.Cliente.PostAsync($"{Opcionais}/{encerrado.Id}/encerrar", null, Ct)).EnsureSuccessStatusCode();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);

        var vitrine = await formando.Cliente.GetFromJsonAsync<List<OpcionalDTO>>(Opcionais, Json, Ct);

        vitrine.ShouldNotBeNull().Select(item => item.Id).ShouldBe([aberto.Id]);
        vitrine[0].ValorEmCentavos.ShouldBe(18_000);
        comPrazoVencido.Id.ShouldNotBe(aberto.Id);
    }

    /// <summary>
    /// O item opcional volta marcado no detalhe do plano — é dali que a cartão Opcionais o separa.
    /// </summary>
    /// <remarks>
    /// Sem <c>opcional</c> na projeção do plano, a cartão Opcionais nasce vazia e o item aparece na
    /// lista do plano, ao lado da mensalidade, como se cobrasse a turma inteira.
    /// </remarks>
    [Fact]
    public async Task Detalhe_do_plano_marca_o_item_opcional()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite(estoque: 40, limite: 4));

        var planos = await turma.Presidente.Cliente.GetFromJsonAsync<List<PlanoDeCobrancaResumoDTO>>("/api/v1/cobrancas/planos", Json, Ct);
        var plano = await turma.Presidente.Cliente.GetFromJsonAsync<PlanoDeCobrancaDTO>($"/api/v1/cobrancas/planos/{planos!.Single().Id}", Json, Ct);

        var opcional = plano!.Itens.Single(i => i.Id == item.Id);
        opcional.Opcional.ShouldBeTrue();
        opcional.Estoque.ShouldBe(40);
        opcional.LimitePorFormando.ShouldBe(4);
        plano.Itens.Count(i => !i.Opcional).ShouldBe(1);
    }

    /// <summary>
    /// O teste da sprint: 50 pedidos concorrentes num item com 10 unidades vendem exatamente 10.
    /// </summary>
    /// <remarks>
    /// Cada formando pede uma unidade ao mesmo tempo. Quem decide é o <c>SELECT … FOR UPDATE</c> da
    /// linha do item — a checagem acontece <b>sob a trava</b>, depois de a operação concorrente já
    /// ter consumido a última —, e o <c>CHECK</c> do banco é a rede embaixo dela.
    /// <para>
    /// Cinquenta formandos com adesão levam tempo demais para um teste; dezesseis provam a mesma
    /// coisa, porque o que se prova é que <b>nenhum</b> passa do teto. A montagem é sequencial de
    /// propósito: criar conta em paralelo esbarra no limitador de taxa do <c>auth</c> e devolve 429
    /// antes de o teste chegar ao que interessa. O paralelismo é só no <c>POST /pedidos</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Pedidos_concorrentes_nunca_passam_do_estoque()
    {
        // Arrange
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite(estoque: 10));
        var formandos = new List<MembroDeTeste>();

        for (var quantos = 0; quantos < 16; quantos++)
            formandos.Add(await Formando(turma.FormaturaId));

        // Act
        var respostas = await Task.WhenAll(
            formandos.Select(formando => formando.Cliente.PostAsJsonAsync(Pedidos, new PedidoRequestDTO(item.Id, 1), Json, Ct))
        );

        // Assert
        var confirmados = respostas.Count(resposta => resposta.StatusCode == HttpStatusCode.OK);
        confirmados.ShouldBe(10);
        foreach (var recusada in respostas.Where(resposta => resposta.StatusCode != HttpStatusCode.OK))
        {
            recusada.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await recusada.Codigo(Ct)).ShouldBe("cobranca.estoque_esgotado");
        }

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        (await contexto.ItensDeCobranca.SingleAsync(i => i.Id == item.Id, Ct)).Reservados.ShouldBe(10);
        (await contexto.Pedidos.CountAsync(p => p.ItemDeCobrancaId == item.Id && p.Status == StatusDoPedido.Confirmado, Ct)).ShouldBe(10);
    }

    /// <summary>Clique duplo: o índice único <c>(vínculo, item)</c> faz dos dois um pedido só.</summary>
    [Fact]
    public async Task Dois_pedidos_iguais_em_paralelo_criam_um_pedido_so()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite());
        var formando = await Formando(turma.FormaturaId);

        var corpo = new PedidoRequestDTO(item.Id, 2);
        await Task.WhenAll(formando.Cliente.PostAsJsonAsync(Pedidos, corpo, Json, Ct), formando.Cliente.PostAsJsonAsync(Pedidos, corpo, Json, Ct));

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var pedidos = await contexto.Pedidos.Where(p => p.ItemDeCobrancaId == item.Id).ToListAsync(Ct);
        pedidos.Count.ShouldBe(1);
        pedidos[0].Quantidade.ShouldBe(2);
        (await contexto.ItensDeCobranca.SingleAsync(i => i.Id == item.Id, Ct)).Reservados.ShouldBe(2);
    }

    /// <summary>
    /// A garantia, direto contra a constraint: nem <c>UPDATE</c> na mão vende o convite 81.
    /// </summary>
    [Fact]
    public async Task Check_do_banco_aborta_reservados_invalidos()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite(estoque: 5));

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);

        var acima = await Should.ThrowAsync<PostgresException>(async () =>
            await contexto.Database.ExecuteSqlAsync($"UPDATE itens_de_cobranca SET reservados = 6 WHERE id = {item.Id}", Ct)
        );
        var negativo = await Should.ThrowAsync<PostgresException>(async () =>
            await contexto.Database.ExecuteSqlAsync($"UPDATE itens_de_cobranca SET reservados = -1 WHERE id = {item.Id}", Ct)
        );

        acima.ConstraintName.ShouldBe("ck_itens_de_cobranca_reservados");
        negativo.ConstraintName.ShouldBe("ck_itens_de_cobranca_reservados");
    }

    /// <summary>A parcela do pedido é uma parcela como as outras: ela aparece no extrato sem tratamento especial.</summary>
    [Fact]
    public async Task Pedido_gera_as_parcelas_e_elas_entram_no_extrato()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite());
        var formando = await Formando(turma.FormaturaId);

        var resposta = await formando.Cliente.PostAsJsonAsync(Pedidos, new PedidoRequestDTO(item.Id, 3, 2), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var pedido = (await resposta.Content.ReadFromJsonAsync<PedidoDTO>(Json, Ct))!;
        pedido.Quantidade.ShouldBe(3);
        pedido.Parcelas.ShouldBe(2);
        pedido.TotalEmCentavos.ShouldBe(54_000);
        pedido.Quitado.ShouldBeFalse();

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var parcelas = await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == item.Id).OrderBy(p => p.Numero).ToListAsync(Ct);
        parcelas.Count.ShouldBe(2);
        parcelas.ShouldAllBe(parcela => parcela.ValorOriginalEmCentavos == 27_000);
        parcelas.ShouldAllBe(parcela => parcela.Vencimento >= DataUtils.Hoje());
    }

    /// <summary>O teto do item não obriga ninguém a parcelar: sem escolha, o pedido é à vista.</summary>
    [Fact]
    public async Task Pedido_sem_parcelas_e_a_vista_e_acima_do_teto_e_recusado()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite());
        var formando = await Formando(turma.FormaturaId);

        var acima = await formando.Cliente.PostAsJsonAsync(Pedidos, new PedidoRequestDTO(item.Id, 1, 3), Json, Ct);
        var aVista = await formando.Cliente.PostAsJsonAsync(Pedidos, new PedidoRequestDTO(item.Id, 2), Json, Ct);

        acima.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await acima.Codigo(Ct)).ShouldBe("cobranca.parcelas_acima_do_teto");
        aVista.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await aVista.Content.ReadFromJsonAsync<PedidoDTO>(Json, Ct))!.Parcelas.ShouldBe(1);
        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var parcelas = await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == item.Id).ToListAsync(Ct);
        parcelas.Count.ShouldBe(1);
        parcelas[0].ValorOriginalEmCentavos.ShouldBe(36_000);
    }

    /// <summary>Aumentar acrescenta só o que falta, com a numeração continuando de onde parou.</summary>
    [Fact]
    public async Task Aumentar_a_quantidade_continua_a_numeracao()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite());
        var formando = await Formando(turma.FormaturaId);
        var pedido = await Pedir(formando, item.Id, 1);

        var ajuste = await formando.Cliente.PutAsJsonAsync($"{Pedidos}/{pedido.Id}", new QuantidadeDoPedidoRequestDTO(3), Json, Ct);

        ajuste.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var parcelas = await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == item.Id).OrderBy(p => p.Numero).ToListAsync(Ct);
        parcelas.Select(p => p.Numero).ShouldBe([1, 2, 3, 4]);
        parcelas.Sum(p => p.ValorOriginalEmCentavos).ShouldBe(54_000);
        (await contexto.ItensDeCobranca.SingleAsync(i => i.Id == item.Id, Ct)).Reservados.ShouldBe(3);
    }

    /// <summary>Quantidade absoluta: o mesmo <c>PUT</c> repetido reserva delta zero.</summary>
    [Fact]
    public async Task Put_repetido_nao_reserva_duas_vezes()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite());
        var formando = await Formando(turma.FormaturaId);
        var pedido = await Pedir(formando, item.Id, 1);

        var corpo = new QuantidadeDoPedidoRequestDTO(2);
        (await formando.Cliente.PutAsJsonAsync($"{Pedidos}/{pedido.Id}", corpo, Json, Ct)).EnsureSuccessStatusCode();
        (await formando.Cliente.PutAsJsonAsync($"{Pedidos}/{pedido.Id}", corpo, Json, Ct)).EnsureSuccessStatusCode();

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        (await contexto.ItensDeCobranca.SingleAsync(i => i.Id == item.Id, Ct)).Reservados.ShouldBe(2);
        (await contexto.Parcelas.CountAsync(p => p.ItemDeCobrancaId == item.Id && p.Status == StatusDaParcela.Aberta, Ct)).ShouldBe(4);
    }

    [Fact]
    public async Task Cancelar_devolve_o_estoque_e_cancelar_de_novo_nao_devolve_outra_vez()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite(estoque: 10));
        var formando = await Formando(turma.FormaturaId);
        var pedido = await Pedir(formando, item.Id, 3);

        (await formando.Cliente.PostAsync($"{Pedidos}/{pedido.Id}/cancelar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await formando.Cliente.PostAsync($"{Pedidos}/{pedido.Id}/cancelar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        (await contexto.ItensDeCobranca.SingleAsync(i => i.Id == item.Id, Ct)).Reservados.ShouldBe(0);
        (await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == item.Id).ToListAsync(Ct)).ShouldAllBe(p => p.Status == StatusDaParcela.Cancelada);
    }

    /// <summary>Antes da abertura, a vitrine mostra a data e a API recusa — sem contagem regressiva.</summary>
    [Fact]
    public async Task Pedido_antes_da_abertura_devolve_venda_nao_aberta()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite(abertura: DataUtils.Hoje().AddDays(5)));
        var formando = await Formando(turma.FormaturaId);

        var resposta = await formando.Cliente.PostAsJsonAsync(Pedidos, new PedidoRequestDTO(item.Id, 1), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("cobranca.venda_nao_aberta");
        var vitrine = await formando.Cliente.GetFromJsonAsync<List<OpcionalDTO>>(Opcionais, Json, Ct);
        vitrine!.Single(i => i.Id == item.Id).AbertoAPedido.ShouldBeFalse();
    }

    /// <summary>Quem não aceitou o termo não passa a dever por um caminho lateral (decisão 4).</summary>
    [Fact]
    public async Task Quem_nao_aderiu_recebe_pedido_sem_adesao()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite());
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);

        var resposta = await formando.Cliente.PostAsJsonAsync(Pedidos, new PedidoRequestDTO(item.Id, 1), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("cobranca.pedido_sem_adesao");
    }

    /// <summary>O pedido de outro responde 404, nunca 403.</summary>
    [Fact]
    public async Task Pedido_de_outro_formando_responde_404()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite());
        var dono = await Formando(turma.FormaturaId);
        var outro = await Formando(turma.FormaturaId);
        var pedido = await Pedir(dono, item.Id, 1);

        var ajuste = await outro.Cliente.PutAsJsonAsync($"{Pedidos}/{pedido.Id}", new QuantidadeDoPedidoRequestDTO(2), Json, Ct);
        var cancelamento = await outro.Cliente.PostAsync($"{Pedidos}/{pedido.Id}/cancelar", null, Ct);

        ajuste.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        cancelamento.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>A lista da turma é da Gestão: é ela que responde ao formando que diz "pedi e não apareceu".</summary>
    [Theory]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Lista_da_turma_segue_a_politica_de_gestao(string papel, HttpStatusCode esperado)
    {
        var turma = await TurmaComPlano();
        var membro = await fabrica.NovoMembro(turma.FormaturaId, papel, Ct);

        (await membro.Cliente.GetAsync(Pedidos, Ct)).StatusCode.ShouldBe(esperado);
        (await membro.Cliente.GetAsync($"{Pedidos}/resumo", Ct)).StatusCode.ShouldBe(esperado);
    }

    /// <summary>A faixa da Gestão mostra a conta aberta: pedidos, unidades, quitadas e o que resta.</summary>
    [Fact]
    public async Task Resumo_da_gestao_abre_a_conta_do_item()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite(estoque: 20));
        var formando = await Formando(turma.FormaturaId);
        await Pedir(formando, item.Id, 3);

        var resumo = await turma.Presidente.Cliente.GetFromJsonAsync<List<ResumoDoItemPedidoDTO>>($"{Pedidos}/resumo", Json, Ct);

        var linha = resumo!.Single(r => r.ItemDeCobrancaId == item.Id);
        linha.Pedidos.ShouldBe(1);
        linha.Unidades.ShouldBe(3);
        linha.UnidadesQuitadas.ShouldBe(0);
        linha.Disponivel.ShouldBe(17);
        linha.TotalEmCentavos.ShouldBe(54_000);
    }

    /// <summary>As pílulas "Pagos" e "Aguardando" da tela: a mesma conta de <c>Quitado</c>, feita no banco.</summary>
    [Fact]
    public async Task Lista_filtra_por_quitado()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite());
        var pagou = await Formando(turma.FormaturaId);
        var deve = await Formando(turma.FormaturaId);
        var pago = await Pedir(pagou, item.Id, 1, parcelas: 1);
        var devido = await Pedir(deve, item.Id, 1, parcelas: 1);

        await using (var contexto = fabrica.ContextoDe(turma.FormaturaId))
        {
            var vinculoId = await contexto.Vinculos.Where(v => v.UsuarioId == pagou.UsuarioId).Select(v => v.Id).SingleAsync(Ct);
            await contexto
                .Parcelas.Where(p => p.ItemDeCobrancaId == item.Id && p.VinculoId == vinculoId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ValorPagoEmCentavos, p => p.ValorOriginalEmCentavos), Ct);
        }

        async Task<IEnumerable<Guid>> Ids(bool quitado) =>
            (
                await turma.Presidente.Cliente.GetFromJsonAsync<PaginaDTO<PedidoDTO>>(
                    $"{Pedidos}?item_de_cobranca_id={item.Id}&quitado={quitado}",
                    Json,
                    Ct
                )
            )!.Itens.Select(p => p.Id);

        (await Ids(true)).ShouldBe([pago.Id]);
        (await Ids(false)).ShouldBe([devido.Id]);
    }

    /// <summary>Item opcional já pedido não se exclui: encerra-se, como o fornecedor em uso.</summary>
    [Fact]
    public async Task Excluir_item_com_pedido_devolve_409()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite());
        var formando = await Formando(turma.FormaturaId);
        await Pedir(formando, item.Id, 1);

        var exclusao = await turma.Presidente.Cliente.DeleteAsync($"{Opcionais}/{item.Id}", Ct);

        exclusao.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await exclusao.Codigo(Ct)).ShouldBe("cobranca.item_com_pedido");
    }

    /// <summary>P8: o salão encolheu, mas quem escolhe qual pedido cai é a comissão.</summary>
    [Fact]
    public async Task Reduzir_o_estoque_abaixo_do_reservado_devolve_409()
    {
        var turma = await TurmaComPlano();
        var item = await CriarItem(turma.Presidente.Cliente, Convite(estoque: 10));
        var formando = await Formando(turma.FormaturaId);
        await Pedir(formando, item.Id, 4);

        var ajuste = await turma.Presidente.Cliente.PutAsJsonAsync($"{Opcionais}/{item.Id}", Convite(estoque: 2), Json, Ct);

        ajuste.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ajuste.Codigo(Ct)).ShouldBe("cobranca.estoque_menor_que_reservado");
    }

    // ---- Decisão 11: o vínculo com o item da festa ----

    /// <summary>O cartão da festa troca a estimativa pelo que a turma de fato vendeu — sem ninguém editar o item.</summary>
    [Fact]
    public async Task Opcional_ligado_muda_o_custo_do_item_da_festa()
    {
        var turma = await TurmaComPlano();
        var foto = await ItemDaFesta(turma.Presidente, TipoDeRateio.PorFormando, previsto: 35_000, estimados: 40);
        var antes = await ItemDaFestaAtual(turma.Presidente, foto.Id);

        await CriarItem(turma.Presidente.Cliente, Convite(preco: 35_000, itemDaFestaId: foto.Id));
        var comOpcional = await ItemDaFestaAtual(turma.Presidente, foto.Id);

        var formando = await Formando(turma.FormaturaId);
        var item = (await turma.Presidente.Cliente.GetFromJsonAsync<List<OpcionalDTO>>(Opcionais, Json, Ct))!.Single();
        await Pedir(formando, item.Id, 2);
        var comPedido = await ItemDaFestaAtual(turma.Presidente, foto.Id);

        antes.CustoEmCentavos.ShouldBe(14_000_00);
        comOpcional.PrecoDeVendaEmCentavos.ShouldBe(35_000);
        comOpcional.CustoEmCentavos.ShouldBe(0);
        comPedido.PedidosConfirmados.ShouldBe(2);
        comPedido.CustoEmCentavos.ShouldBe(70_000);
    }

    /// <summary>Um item da festa tem no máximo um item opcional — o índice único parcial aborta o segundo.</summary>
    [Fact]
    public async Task Dois_itens_de_opcional_no_mesmo_item_da_festa_sao_recusados()
    {
        var turma = await TurmaComPlano();
        var foto = await ItemDaFesta(turma.Presidente, TipoDeRateio.PorFormando, previsto: 35_000, estimados: 40);
        await CriarItem(turma.Presidente.Cliente, Convite(itemDaFestaId: foto.Id));

        var segundo = await turma.Presidente.Cliente.PostAsJsonAsync(Opcionais, Convite(itemDaFestaId: foto.Id), Json, Ct);

        segundo.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await segundo.Codigo(Ct)).ShouldBe("cobranca.item_da_festa_invalido");
    }

    /// <summary>
    /// A segunda barreira do vínculo, direto contra o índice: nem um <c>INSERT</c> na mão passa.
    /// </summary>
    /// <remarks>
    /// O service recusa antes, com 400 — mas o índice único <b>parcial</b> é o que garante que dois
    /// itens opcionais jamais apontem para a mesma foto. Parcial porque o nulo é a maioria: sem o
    /// filtro, o segundo item sem vínculo já colidiria com o primeiro.
    /// </remarks>
    [Fact]
    public async Task Indice_unico_impede_dois_opcionais_no_mesmo_item_da_festa()
    {
        var turma = await TurmaComPlano();
        var foto = await ItemDaFesta(turma.Presidente, TipoDeRateio.PorFormando, previsto: 35_000, estimados: 40);
        await CriarItem(turma.Presidente.Cliente, Convite(itemDaFestaId: foto.Id));
        var outro = await CriarItem(turma.Presidente.Cliente, Convite());
        var semVinculo = await CriarItem(turma.Presidente.Cliente, Convite());

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);

        var colisao = await Should.ThrowAsync<PostgresException>(async () =>
            await contexto.Database.ExecuteSqlAsync($"UPDATE itens_de_cobranca SET item_da_festa_id = {foto.Id} WHERE id = {outro.Id}", Ct)
        );

        colisao.ConstraintName.ShouldBe("ix_itens_de_cobranca_item_da_festa");
        // O nulo não colide com o nulo: é o que o filtro parcial do índice garante.
        semVinculo.ItemDaFestaId.ShouldBeNull();
    }

    /// <summary>Mensalidade da turma não é "o que só alguns compram": rateio <c>Turma</c> é recusado.</summary>
    [Fact]
    public async Task Vincular_a_item_rateado_pela_turma_e_recusado()
    {
        var turma = await TurmaComPlano();
        var buffet = await ItemDaFesta(turma.Presidente, TipoDeRateio.Turma, previsto: 60_000_00, estimados: 1);

        var resposta = await turma.Presidente.Cliente.PostAsJsonAsync(Opcionais, Convite(itemDaFestaId: buffet.Id), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await resposta.Codigo(Ct)).ShouldBe("cobranca.item_da_festa_invalido");
    }

    /// <summary>Desistir da foto com venda aberta zeraria o custo e deixaria o dinheiro pago no arrecadado.</summary>
    [Fact]
    public async Task Cancelar_item_da_festa_com_opcional_devolve_409()
    {
        var turma = await TurmaComPlano();
        var foto = await ItemDaFesta(turma.Presidente, TipoDeRateio.PorFormando, previsto: 35_000, estimados: 40);
        var opcionais = await CriarItem(turma.Presidente.Cliente, Convite(itemDaFestaId: foto.Id));

        var comOpcional = await turma.Presidente.Cliente.PostAsync($"/api/v1/festa/itens/{foto.Id}/cancelamento", null, Ct);
        (await turma.Presidente.Cliente.PostAsync($"{Opcionais}/{opcionais.Id}/encerrar", null, Ct)).EnsureSuccessStatusCode();
        var depoisDeEncerrar = await turma.Presidente.Cliente.PostAsync($"/api/v1/festa/itens/{foto.Id}/cancelamento", null, Ct);

        comOpcional.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await comOpcional.Codigo(Ct)).ShouldBe("festa.item_com_opcional");
        depoisDeEncerrar.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Excluir o item da festa não pode ser barrado por alguém ter aberto venda dele: o vínculo vira nulo.</summary>
    [Fact]
    public async Task Excluir_item_da_festa_deixa_o_opcional_de_pe_com_vinculo_nulo()
    {
        var turma = await TurmaComPlano();
        var foto = await ItemDaFesta(turma.Presidente, TipoDeRateio.PorFormando, previsto: 35_000, estimados: 40);
        var opcionais = await CriarItem(turma.Presidente.Cliente, Convite(itemDaFestaId: foto.Id));

        (await turma.Presidente.Cliente.DeleteAsync($"/api/v1/festa/itens/{foto.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var item = await contexto.ItensDeCobranca.SingleAsync(i => i.Id == opcionais.Id, Ct);
        item.ItemDaFestaId.ShouldBeNull();
        item.EncerradoEm.ShouldBeNull();
    }

    // ---- Montagem ----

    private Task<TurmaDeTeste> TurmaComPlano() => fabrica.TurmaComPlano();

    private Task<MembroDeTeste> Formando(Guid formaturaId) => fabrica.FormandoComAdesao(formaturaId);

    private static async Task<ItemDeCobrancaDTO> CriarItem(HttpClient cliente, OpcionalRequestDTO corpo)
    {
        var resposta = await cliente.PostAsJsonAsync(Opcionais, corpo, Json, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await resposta.Content.ReadFromJsonAsync<ItemDeCobrancaDTO>(Json, Ct))!;
    }

    /// <summary>Pede em 2× — a grade inteira do convite do exemplo, que é o que os testes de ajuste medem.</summary>
    private static async Task<PedidoDTO> Pedir(MembroDeTeste formando, Guid itemId, int quantidade, int parcelas = 2)
    {
        var resposta = await formando.Cliente.PostAsJsonAsync(Pedidos, new PedidoRequestDTO(itemId, quantidade, parcelas), Json, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await resposta.Content.ReadFromJsonAsync<PedidoDTO>(Json, Ct))!;
    }

    private static async Task<ItemDaFestaDTO> ItemDaFesta(MembroDeTeste gestao, TipoDeRateio rateio, long previsto, int estimados)
    {
        var corpo = new MultipartFormDataContent
        {
            { new StringContent("Fotografia"), "titulo" },
            { new StringContent(nameof(CategoriaDeDespesa.Fotografia)), "categoria" },
            { new StringContent(rateio.ToString()), "rateio" },
            { new StringContent(previsto.ToString(CultureInfo.InvariantCulture)), "valorPrevistoEmCentavos" },
            { new StringContent(estimados.ToString(CultureInfo.InvariantCulture)), "quantidadeEstimada" },
        };

        var resposta = await gestao.Cliente.PostAsync("/api/v1/festa/itens", corpo, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await resposta.Content.ReadFromJsonAsync<ItemDaFestaDTO>(Json, Ct))!;
    }

    private static async Task<ItemDaFestaDTO> ItemDaFestaAtual(MembroDeTeste gestao, Guid id) =>
        (await gestao.Cliente.GetFromJsonAsync<ItemDaFestaDTO>($"/api/v1/festa/itens/{id}", Json, Ct))!;
}
