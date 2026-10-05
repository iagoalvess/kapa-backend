using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Api.DTOs.Adesoes;
using Backend.Api.DTOs.Cobrancas;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Formaturas.Models;
using Backend.Business.Loja.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Cobrancas;

/// <summary>
/// O ciclo de vida da cesta (Sprint 48) contra a API e o Postgres: solicitar e responder o cancelamento, o rateio
/// escopado, o preço que não fura o contrato, o lançamento avulso e o aditivo.
/// </summary>
/// <remarks>
/// O catálogo é o do exemplo da sprint: Festa 15 (R$ 3.000), Festa 20 (R$ 3.600) e Fotos (R$ 600), em 10×. As
/// fronteiras de autorização ficam aqui — política mal declarada não quebra o build.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed partial class CicloDaCestaEndpointsTests(ApiFactory fabrica)
{
    private const string Solicitacoes = "/api/v1/cobrancas/solicitacoes-de-cancelamento";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ItemDeCobrancaRequestDTO Festa(string nome, long valor, int convites) =>
        new(TipoDeCobranca.Festa, nome, valor, 10, 10, DataUtils.Hoje().AddMonths(1), Grupo: "Festa", ConvitesDaFesta: convites);

    private static ItemDeCobrancaRequestDTO Fotos() => new(TipoDeCobranca.FotoEAlbum, "Fotos", 60_000, 10, 10, DataUtils.Hoje().AddMonths(1));

    /// <summary>A turma do exemplo e o id de cada pacote: Festa 15, Festa 20 e Fotos.</summary>
    private async Task<(TurmaDeTeste Turma, Guid Festa15, Guid Festa20, Guid Fotos, Guid PlanoId)> Catalogo()
    {
        var (turma, pacotes) = await fabrica.TurmaComCatalogo(Festa("Festa 15", 300_000, 15), Festa("Festa 20", 360_000, 20), Fotos());
        var planoId = (await turma.Presidente.Cliente.GetFromJsonAsync<List<PlanoDeCobrancaResumoDTO>>("/api/v1/cobrancas/planos", Json, Ct))!
            .Single()
            .Id;

        return (turma, pacotes[0], pacotes[1], pacotes[2], planoId);
    }

    private static async Task<T> Ler<T>(HttpResponseMessage resposta)
    {
        resposta.IsSuccessStatusCode.ShouldBeTrue(await resposta.Content.ReadAsStringAsync(Ct));

        return (await resposta.Content.ReadFromJsonAsync<T>(Json, Ct))!;
    }

    /// <summary>D8/D12/D9: o formando pede, as parcelas saem da régua, só a tesouraria aprova, e o pacote sai da cesta.</summary>
    [Fact]
    public async Task Pacote_cancelado_pela_tesouraria_a_pedido_do_formando()
    {
        var (turma, festa15, _, fotos, _) = await Catalogo();
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId, festa15, fotos);
        var comissao = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Comissao, Ct);

        var aberta = await Ler<SolicitacaoDeCancelamentoDTO>(
            await formando.Cliente.PostAsJsonAsync(Solicitacoes, new SolicitacaoDeCancelamentoRequestDTO(fotos, "vou fotografar por conta"), Json, Ct)
        );
        var repetida = await Ler<SolicitacaoDeCancelamentoDTO>(
            await formando.Cliente.PostAsJsonAsync(Solicitacoes, new SolicitacaoDeCancelamentoRequestDTO(fotos, null), Json, Ct)
        );

        aberta.Status.ShouldBe(StatusDoPedidoDeCancelamento.Aberto);
        aberta.RespostaAte.ShouldBe(DataUtils.Hoje().AddDays(SolicitacaoDeCancelamento.DiasParaResponder));
        repetida.Id.ShouldBe(aberta.Id);
        await using (var contexto = fabrica.ContextoDe(turma.FormaturaId))
            (await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == fotos).ToListAsync(Ct)).ShouldAllBe(p => p.SuspensaAte == aberta.RespostaAte);

        (await formando.Cliente.GetAsync(Solicitacoes, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Ler<List<SolicitacaoDeCancelamentoDTO>>(await comissao.Cliente.GetAsync($"{Solicitacoes}?status=Aberto", Ct))).ShouldContain(s =>
            s.Id == aberta.Id
        );
        (await comissao.Cliente.PostAsync($"{Solicitacoes}/{aberta.Id}/aprovar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.Cliente.PostAsync($"{Solicitacoes}/{aberta.Id}/aprovar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var aprovada = await Ler<SolicitacaoDeCancelamentoDTO>(
            await turma.Presidente.Cliente.PostAsync($"{Solicitacoes}/{aberta.Id}/aprovar", null, Ct)
        );
        var outraVez = await turma.Presidente.Cliente.PostAsync($"{Solicitacoes}/{aberta.Id}/aprovar", null, Ct);

        aprovada.Status.ShouldBe(StatusDoPedidoDeCancelamento.Aprovado);
        outraVez.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await outraVez.Codigo(Ct)).ShouldBe("cobranca.solicitacao_ja_respondida");
        await using var depois = fabrica.ContextoDe(turma.FormaturaId);
        (await depois.EscolhasDaCesta.Where(e => e.ItemDeCobrancaId == fotos).CountAsync(Ct)).ShouldBe(0);
        (await depois.EscolhasDaCesta.Where(e => e.ItemDeCobrancaId == festa15).CountAsync(Ct)).ShouldBe(1);
        (await depois.Parcelas.Where(p => p.ItemDeCobrancaId == fotos).ToListAsync(Ct)).ShouldAllBe(p => p.Status == StatusDaParcela.Cancelada);
    }

    /// <summary>Recusar exige motivo e devolve a cobrança no mesmo dia; o formando lê a resposta.</summary>
    [Fact]
    public async Task Recusa_retoma_a_cobranca_e_o_formando_le_o_motivo()
    {
        var (turma, festa15, _, _, _) = await Catalogo();
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId, festa15);
        var aberta = await Ler<SolicitacaoDeCancelamentoDTO>(
            await formando.Cliente.PostAsJsonAsync(Solicitacoes, new SolicitacaoDeCancelamentoRequestDTO(festa15, null), Json, Ct)
        );

        var semMotivo = await turma.Presidente.Cliente.PostAsJsonAsync(
            $"{Solicitacoes}/{aberta.Id}/recusar",
            new RecusaDaSolicitacaoRequestDTO(" "),
            Json,
            Ct
        );
        var recusada = await turma.Presidente.Cliente.PostAsJsonAsync(
            $"{Solicitacoes}/{aberta.Id}/recusar",
            new RecusaDaSolicitacaoRequestDTO("o buffet já foi contratado"),
            Json,
            Ct
        );

        semMotivo.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        recusada.StatusCode.ShouldBe(HttpStatusCode.OK);
        var minhas = await Ler<List<SolicitacaoDeCancelamentoDTO>>(await formando.Cliente.GetAsync($"{Solicitacoes}/minhas", Ct));
        minhas.ShouldHaveSingleItem().MotivoDaResposta.ShouldBe("o buffet já foi contratado");
        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        (await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == festa15).ToListAsync(Ct)).ShouldAllBe(p => p.SuspensaAte == null);
    }

    /// <summary>D19: o custo da festa só alcança quem tem a festa — a conta antes de confirmar diz quantos.</summary>
    [Fact]
    public async Task Rateio_com_alvo_so_cobra_quem_tem_a_festa()
    {
        var (turma, festa15, festa20, fotos, planoId) = await Catalogo();
        var daFesta = await fabrica.FormandoComAdesao(turma.FormaturaId, festa15, fotos);
        var soFotos = await fabrica.FormandoComAdesao(turma.FormaturaId, fotos);

        var alcance = await Ler<AlcanceDTO>(
            await turma.Presidente.Cliente.GetAsync(
                $"/api/v1/cobrancas/planos/{planoId}/alcance-do-rateio?alvo={festa15}&alvo={festa20}&valor_em_centavos=5000",
                Ct
            )
        );
        var plano = await Ler<PlanoDeCobrancaDTO>(
            await turma.Presidente.Cliente.PostAsJsonAsync(
                $"/api/v1/cobrancas/planos/{planoId}/itens",
                new ItemDeCobrancaRequestDTO(
                    TipoDeCobranca.Avulsa,
                    "Animadores",
                    5_000,
                    1,
                    10,
                    DataUtils.Hoje().AddMonths(1),
                    AplicarAQuemJaAderiu: true,
                    OrigemDaDecisao: "assembleia de 12/10",
                    Alvo: [festa15, festa20]
                ),
                Json,
                Ct
            )
        );

        alcance.ShouldBe(new AlcanceDTO(1, 0, 5_000));
        var animadores = plano.Itens.Single(item => item.Descricao == "Animadores");
        animadores.AlvoDoRateio.ShouldBe([festa15, festa20], ignoreOrder: true);
        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var vinculos = await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == animadores.Id).Select(p => p.VinculoId).Distinct().ToListAsync(Ct);
        var vinculoDaFesta = await contexto.Vinculos.Where(v => v.UsuarioId == daFesta.UsuarioId).Select(v => v.Id).SingleAsync(Ct);
        vinculos.ShouldBe([vinculoDaFesta]);
        (await contexto.Vinculos.Where(v => v.UsuarioId == soFotos.UsuarioId).Select(v => v.Id).SingleAsync(Ct)).ShouldNotBe(vinculoDaFesta);
    }

    /// <summary>D21: o preço novo vale para quem aderir depois; marcado, repactua quem já aderiu no que não venceu.</summary>
    [Fact]
    public async Task Preco_novo_so_alcanca_quem_ja_aderiu_quando_marcado()
    {
        var (turma, _, _, fotos, planoId) = await Catalogo();
        await fabrica.FormandoComAdesao(turma.FormaturaId, fotos);
        var rota = $"/api/v1/cobrancas/planos/{planoId}/itens/{fotos}";

        var alcance = await Ler<AlcanceDTO>(await turma.Presidente.Cliente.GetAsync($"{rota}/alcance-do-preco?valor_em_centavos=70000", Ct));
        (await turma.Presidente.Cliente.PutAsJsonAsync(rota, Fotos() with { ValorEmCentavos = 70_000 }, Json, Ct)).EnsureSuccessStatusCode();

        alcance.Formandos.ShouldBe(1);
        alcance.TotalEmCentavos.ShouldBe(10_000);
        await using (var contexto = fabrica.ContextoDe(turma.FormaturaId))
            (await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == fotos).SumAsync(p => p.ValorOriginalEmCentavos, Ct)).ShouldBe(60_000);

        (
            await turma.Presidente.Cliente.PutAsJsonAsync(rota, Fotos() with { ValorEmCentavos = 80_000, AplicarAosAtuais = true }, Json, Ct)
        ).EnsureSuccessStatusCode();

        await using var depois = fabrica.ContextoDe(turma.FormaturaId);
        (await depois.Parcelas.Where(p => p.ItemDeCobrancaId == fotos).SumAsync(p => p.ValorOriginalEmCentavos, Ct)).ShouldBe(70_000);
    }

    /// <summary>D23: a tesouraria lança no vínculo, positivo ou negativo, e vira parcela no extrato do formando.</summary>
    [Fact]
    public async Task Lancamento_avulso_e_da_tesouraria_e_entra_no_extrato()
    {
        var (turma, festa15, _, _, _) = await Catalogo();
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId, festa15);
        var bolsa = new LancamentoAvulsoRequestDTO(formando.UsuarioId, "Bolsa da comissão", -30_000, 3, DataUtils.Hoje().AddDays(5));

        var peloFormando = await formando.Cliente.PostAsJsonAsync("/api/v1/cobrancas/avulsas", bolsa, Json, Ct);
        var retroativo = await turma.Presidente.Cliente.PostAsJsonAsync(
            "/api/v1/cobrancas/avulsas",
            bolsa with
            {
                PrimeiroVencimento = DataUtils.Hoje().AddDays(-1),
            },
            Json,
            Ct
        );
        var lancado = await Ler<LancamentoDTO>(await turma.Presidente.Cliente.PostAsJsonAsync("/api/v1/cobrancas/avulsas", bolsa, Json, Ct));

        peloFormando.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await retroativo.Codigo(Ct)).ShouldBe("cobranca.lancamento_retroativo");
        lancado.ValorEmCentavos.ShouldBe(-30_000);
        (await Ler<List<LancamentoDTO>>(await turma.Presidente.Cliente.GetAsync("/api/v1/cobrancas/avulsas", Ct))).ShouldContain(l =>
            l.ItemDeCobrancaId == lancado.ItemDeCobrancaId
        );
        (
            await Ler<PlanoDeCobrancaDTO>(await turma.Presidente.Cliente.GetAsync($"/api/v1/cobrancas/planos/{lancado.PlanoId}", Ct))
        ).Itens.ShouldNotContain(item => item.Id == lancado.ItemDeCobrancaId);
        (await formando.Cliente.GetStringAsync("/api/v1/extrato/eu", Ct)).ShouldContain("Bolsa da comissão");
        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        (
            await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == lancado.ItemDeCobrancaId).Select(p => p.ValorOriginalEmCentavos).ToListAsync(Ct)
        ).ShouldBe([-10_000, -10_000, -10_000], ignoreOrder: true);
    }

    /// <summary>
    /// D38: subir da Festa 15 para a 20 é um aditivo com o rito da adesão, que cobra só a diferença; descer não é aditivo.
    /// </summary>
    [Fact]
    public async Task Aditivo_sobe_de_faixa_cobrando_so_a_diferenca()
    {
        var (turma, festa15, festa20, _, _) = await Catalogo();
        var formando = await fabrica.FormandoComAdesao(turma.FormaturaId, festa15);

        var cesta = await Ler<MinhaCestaDTO>(await formando.Cliente.GetAsync("/api/v1/adesoes/minha-cesta", Ct));
        var previa = await Ler<PreviaDoAditivoDTO>(
            await formando.Cliente.PostAsJsonAsync("/api/v1/adesoes/aditivo/previa", new SimularAditivoRequestDTO([festa20]), Json, Ct)
        );
        (await formando.Cliente.PostAsync("/api/v1/adesoes/aditivo/codigo", null, Ct)).EnsureSuccessStatusCode();
        var codigo = SeisDigitos().Match((await fabrica.UltimoEmailDaFila(Ct))!).Value;

        var errado = await formando.Cliente.PostAsJsonAsync(
            "/api/v1/adesoes/aditivo",
            new AceitarAditivoRequestDTO([festa20], previa.HashDoConteudo, codigo == "000000" ? "111111" : "000000"),
            Json,
            Ct
        );
        var aceito = await formando.Cliente.PostAsJsonAsync(
            "/api/v1/adesoes/aditivo",
            new AceitarAditivoRequestDTO([festa20], previa.HashDoConteudo, codigo, [new ObservacaoDoPacoteDTO(festa20, "mesa perto da pista")]),
            Json,
            Ct
        );

        cesta.Disponiveis.ShouldContain(p => p.ItemDeCobrancaId == festa20 && p.DiferencaEmCentavos == 60_000 && p.Substitui == festa15);
        previa.TotalEmCentavos.ShouldBe(60_000);
        previa.Mudancas.ShouldHaveSingleItem().Sai!.ItemId.ShouldBe(festa15);
        (await errado.Codigo(Ct)).ShouldBe("adesao.codigo_invalido");
        aceito.StatusCode.ShouldBe(HttpStatusCode.OK, await aceito.Content.ReadAsStringAsync(Ct));

        var depois = await Ler<MinhaCestaDTO>(await formando.Cliente.GetAsync("/api/v1/adesoes/minha-cesta", Ct));
        depois.Pacotes.Select(p => p.ItemDeCobrancaId).ShouldBe([festa20]);
        depois.Pacotes[0].Observacao.ShouldBe("mesa perto da pista");
        var descer = await formando.Cliente.PostAsJsonAsync("/api/v1/adesoes/aditivo/previa", new SimularAditivoRequestDTO([festa15]), Json, Ct);
        (await descer.Codigo(Ct)).ShouldBe("adesao.aditivo_so_acrescenta");

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        (await contexto.AditivosDaAdesao.CountAsync(Ct)).ShouldBe(1);
        (await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == festa20).SumAsync(p => p.ValorOriginalEmCentavos, Ct)).ShouldBe(60_000);
        (await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == festa15).SumAsync(p => p.ValorOriginalEmCentavos, Ct)).ShouldBe(300_000);
    }

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex SeisDigitos();
}
