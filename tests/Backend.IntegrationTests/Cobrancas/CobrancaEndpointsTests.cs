using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Comum;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Services;
using Backend.Business.Common.Datas;
using Backend.Business.Formaturas.Models;
using Backend.Data;
using Backend.Data.Repositories;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;

namespace Backend.IntegrationTests.Cobrancas;

/// <summary>
/// O plano de cobrança contra a API e o Postgres de verdade: quem pode chamar cada endpoint, e as
/// regras que só o banco prova — um vigente por turma, geração idempotente, passado imutável.
/// </summary>
/// <remarks>
/// As parcelas são geradas chamando o <see cref="GeracaoDeParcelasService"/> num contexto da turma,
/// como a adesão faz, sem montar termo e cadastro para cada caso — o fluxo inteiro da adesão está em
/// <c>AdesaoEndpointsTests</c>.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class CobrancaEndpointsTests(ApiFactory fabrica)
{
    private const string Rota = "/api/v1/cobrancas";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ItemDeCobrancaRequestDTO Mensalidade(long valor = 840_000, int parcelas = 24, int dia = 10, DateOnly? primeiroMes = null) =>
        new(TipoDeCobranca.Mensalidade, null, valor, parcelas, dia, primeiroMes ?? new DateOnly(DateTime.UtcNow.Year + 1, 3, 1));

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Planos_seguem_a_politica_de_tesouraria(string papel, HttpStatusCode esperado)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        var lista = await membro.Cliente.GetAsync($"{Rota}/planos", Ct);
        var criacao = await membro.Cliente.PostAsJsonAsync($"{Rota}/planos", new PlanoDeCobrancaRequestDTO("Plano", 0, 0, 0, 0), Json, Ct);

        lista.StatusCode.ShouldBe(esperado);
        criacao.StatusCode.ShouldBe(esperado == HttpStatusCode.OK ? HttpStatusCode.Created : esperado);
    }

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Parcelas_seguem_a_politica_de_gestao(string papel, HttpStatusCode esperado)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        var resposta = await membro.Cliente.GetAsync($"{Rota}/parcelas", Ct);

        resposta.StatusCode.ShouldBe(esperado);
    }

    /// <summary>A tesouraria monta; pôr em vigor é decisão do Presidente.</summary>
    [Fact]
    public async Task Vigorar_e_so_do_presidente()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var plano = await CriarPlano(tesoureiro.Cliente, Mensalidade());

        var doTesoureiro = await tesoureiro.Cliente.PostAsync($"{Rota}/planos/{plano.Id}/vigorar", null, Ct);
        var doPresidente = await presidente.Cliente.PostAsync($"{Rota}/planos/{plano.Id}/vigorar", null, Ct);

        doTesoureiro.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        doPresidente.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await doPresidente.Content.ReadFromJsonAsync<PlanoDeCobrancaDTO>(Json, Ct))!.Status.ShouldBe(StatusDoPlano.Vigente);
    }

    /// <summary>Turma suspensa lê e simula o plano, mas não o altera.</summary>
    [Fact]
    public async Task Escrita_exige_formatura_ativa_e_leitura_nao()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var plano = await CriarPlano(presidente.Cliente, Mensalidade());
        await Suspender(formaturaId);

        var escrita = await presidente.Cliente.PostAsJsonAsync($"{Rota}/planos/{plano.Id}/itens", Mensalidade(), Json, Ct);
        var leitura = await presidente.Cliente.GetAsync($"{Rota}/planos/{plano.Id}", Ct);
        var simulacao = await presidente.Cliente.PostAsJsonAsync($"{Rota}/planos/{plano.Id}/simular", new SimularPlanoRequestDTO(null), Json, Ct);

        escrita.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await escrita.Codigo(Ct)).ShouldBe("formatura.inativa");
        leitura.StatusCode.ShouldBe(HttpStatusCode.OK);
        simulacao.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// A prévia que a tesouraria confere é, parcela por parcela, o que o formando passa a dever —
    /// inclusive o resto na primeira parcela e o dia 31 em fevereiro bissexto.
    /// </summary>
    [Fact]
    public async Task Simulacao_e_identica_a_grade_gerada()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        var plano = await CriarPlano(
            presidente.Cliente,
            Mensalidade(dia: 31, primeiroMes: new DateOnly(2028, 1, 1)),
            new ItemDeCobrancaRequestDTO(TipoDeCobranca.Adesao, "Taxa de adesão", 100_000, 3, 5, new DateOnly(2028, 1, 1))
        );
        await Vigorar(presidente.Cliente, plano.Id);

        var simulacao = await (
            await presidente.Cliente.PostAsJsonAsync($"{Rota}/planos/{plano.Id}/simular", new SimularPlanoRequestDTO(null), Json, Ct)
        ).Content.ReadFromJsonAsync<SimulacaoDoPlanoDTO>(Json, Ct);
        await GerarParcelas(formaturaId, formando.UsuarioId);
        var geradas = await presidente.Cliente.GetFromJsonAsync<PaginaDTO<ParcelaDTO>>(
            $"{Rota}/parcelas?usuarioId={formando.UsuarioId}&tamanho=100",
            Json,
            Ct
        );

        simulacao.ShouldNotBeNull();
        geradas.ShouldNotBeNull();
        simulacao.TotalPorFormando.ShouldBe(940_000);
        simulacao.Formandos.ShouldBe(2);
        simulacao.TotalDaTurma.ShouldBe(1_880_000);
        geradas
            .Itens.Select(p => (p.Tipo, p.Numero, p.Vencimento, p.ValorOriginalEmCentavos))
            .ShouldBe(simulacao.Parcelas.Select(p => (p.Tipo, p.Numero, p.Vencimento, p.ValorEmCentavos)), ignoreOrder: true);
        geradas.Itens.ShouldContain(p => p.Tipo == TipoDeCobranca.Mensalidade && p.Vencimento == new DateOnly(2028, 2, 29));
        geradas.Itens.Single(p => p.Tipo == TipoDeCobranca.Adesao && p.Numero == 1).ValorOriginalEmCentavos.ShouldBe(33_334);
    }

    [Fact]
    public async Task Gerar_parcelas_duas_vezes_cria_uma_grade_so()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        await Vigorar(presidente.Cliente, (await CriarPlano(presidente.Cliente, Mensalidade())).Id);

        var primeira = await GerarParcelas(formaturaId, formando.UsuarioId);
        var segunda = await GerarParcelas(formaturaId, formando.UsuarioId);

        primeira.ShouldBe(24);
        segunda.ShouldBe(0);
        var vinculoId = await VinculoDe(formaturaId, formando.UsuarioId);
        await using var contexto = fabrica.ContextoDe(formaturaId);
        (await contexto.Parcelas.CountAsync(p => p.VinculoId == vinculoId, Ct)).ShouldBe(24);
    }

    /// <summary>A corrida que a leitura prévia não pega: o índice único recusa a segunda cobrança do mês.</summary>
    [Fact]
    public async Task Banco_recusa_a_mesma_parcela_duas_vezes()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        var plano = await CriarPlano(presidente.Cliente, Mensalidade());
        var vinculoId = await VinculoDe(formaturaId, formando.UsuarioId);
        var prevista = new ParcelaPrevista(1, new DateOnly(2030, 1, 10), 35_000);

        await using var contexto = fabrica.ContextoDe(formaturaId);
        contexto.Parcelas.AddRange(Parcela.Nova(vinculoId, plano.Itens[0].Id, prevista), Parcela.Nova(vinculoId, plano.Itens[0].Id, prevista));

        var erro = await Should.ThrowAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Ct));

        erro.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Remover_item_com_parcela_devolve_409_e_encerrar_funciona()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        var plano = await CriarPlano(presidente.Cliente, Mensalidade());
        await Vigorar(presidente.Cliente, plano.Id);
        await GerarParcelas(formaturaId, formando.UsuarioId);
        var item = plano.Itens[0].Id;

        var remocao = await presidente.Cliente.DeleteAsync($"{Rota}/planos/{plano.Id}/itens/{item}", Ct);
        var encerramento = await presidente.Cliente.PostAsync($"{Rota}/planos/{plano.Id}/itens/{item}/encerrar", null, Ct);

        remocao.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await remocao.Codigo(Ct)).ShouldBe("cobranca.item_em_uso");
        encerramento.StatusCode.ShouldBe(HttpStatusCode.OK);
        var encerrado = (await encerramento.Content.ReadFromJsonAsync<PlanoDeCobrancaDTO>(Json, Ct))!.Itens.ShouldHaveSingleItem();
        encerrado.EncerradoEm.ShouldBe(DataUtils.Hoje());
        encerrado.EmUso.ShouldBeTrue();
        var canceladas = await presidente.Cliente.GetFromJsonAsync<PaginaDTO<ParcelaDTO>>($"{Rota}/parcelas?status=Cancelada&tamanho=100", Json, Ct);
        canceladas!.Total.ShouldBe(24);
    }

    /// <summary>
    /// Mudar o valor vale só para o futuro: a vencida e a paga ficam com o valor antigo; as abertas
    /// que ainda não venceram passam ao novo.
    /// </summary>
    [Fact]
    public async Task Alterar_valor_nao_muda_parcela_vencida_nem_paga()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        var hoje = DataUtils.Hoje();
        var inicio = hoje.AddMonths(-3);
        var plano = await CriarPlano(presidente.Cliente, Mensalidade(valor: 120_000, parcelas: 12, dia: 1, primeiroMes: inicio));
        await Vigorar(presidente.Cliente, plano.Id);
        await GerarParcelas(formaturaId, formando.UsuarioId);
        var vinculoId = await VinculoDe(formaturaId, formando.UsuarioId);
        await using (var contexto = fabrica.ContextoDe(formaturaId))
            await contexto.Database.ExecuteSqlAsync($"UPDATE parcelas SET status = 'Paga' WHERE vinculo_id = {vinculoId} AND numero = 12", Ct);

        var alteracao = await presidente.Cliente.PutAsJsonAsync(
            $"{Rota}/planos/{plano.Id}/itens/{plano.Itens[0].Id}",
            Mensalidade(valor: 240_000, parcelas: 12, dia: 1, primeiroMes: inicio),
            Json,
            Ct
        );

        alteracao.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var leitura = fabrica.ContextoDe(formaturaId);
        var parcelas = await leitura.Parcelas.Where(p => p.VinculoId == vinculoId).ToListAsync(Ct);
        parcelas.ShouldContain(p => p.Vencimento < hoje);
        foreach (var parcela in parcelas)
        {
            var esperado = parcela.Numero == 12 || parcela.Vencimento < hoje ? 10_000 : 20_000;
            parcela.ValorOriginalEmCentavos.ShouldBe(esperado, $"parcela {parcela.Numero}, vence {parcela.Vencimento}");
        }
    }

    [Fact]
    public async Task Item_em_uso_nao_muda_o_numero_de_parcelas()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        var plano = await CriarPlano(presidente.Cliente, Mensalidade());
        await Vigorar(presidente.Cliente, plano.Id);
        await GerarParcelas(formaturaId, formando.UsuarioId);

        var resposta = await presidente.Cliente.PutAsJsonAsync(
            $"{Rota}/planos/{plano.Id}/itens/{plano.Itens[0].Id}",
            Mensalidade(parcelas: 12),
            Json,
            Ct
        );

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("cobranca.item_em_uso");
    }

    [Fact]
    public async Task Dois_planos_vigentes_sao_impossiveis()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var primeiro = await CriarPlano(presidente.Cliente, Mensalidade());
        var segundo = await CriarPlano(presidente.Cliente, Mensalidade());
        await Vigorar(presidente.Cliente, primeiro.Id);

        var pelaApi = await presidente.Cliente.PostAsync($"{Rota}/planos/{segundo.Id}/vigorar", null, Ct);

        pelaApi.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await pelaApi.Codigo(Ct)).ShouldBe("cobranca.plano_vigente_existente");
        await using var contexto = fabrica.ContextoDe(formaturaId);
        var peloBanco = await Should.ThrowAsync<PostgresException>(() =>
            contexto.Database.ExecuteSqlAsync($"UPDATE planos_de_cobranca SET status = 'Vigente' WHERE id = {segundo.Id}", Ct)
        );
        peloBanco.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Segunda_adesao_no_plano_devolve_409()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var adesao = new ItemDeCobrancaRequestDTO(TipoDeCobranca.Adesao, null, 50_000, 1, 10, new DateOnly(2030, 1, 1));
        var plano = await CriarPlano(presidente.Cliente, adesao);

        var resposta = await presidente.Cliente.PostAsJsonAsync($"{Rota}/planos/{plano.Id}/itens", adesao, Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("cobranca.adesao_duplicada");
    }

    /// <summary>Plano e parcelas herdam o isolamento: outra turma não os enxerga nem pelo id.</summary>
    [Fact]
    public async Task Plano_e_parcelas_de_outra_formatura_nao_aparecem()
    {
        var formaturaA = await fabrica.CriarFormatura(Ct);
        var presidenteA = await fabrica.NovoMembro(formaturaA, PapelNaFormatura.Presidente, Ct);
        var formandoA = await fabrica.NovoMembro(formaturaA, PapelNaFormatura.Formando, Ct);
        var presidenteB = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var plano = await CriarPlano(presidenteA.Cliente, Mensalidade());
        await Vigorar(presidenteA.Cliente, plano.Id);
        await GerarParcelas(formaturaA, formandoA.UsuarioId);

        var planoPorB = await presidenteB.Cliente.GetAsync($"{Rota}/planos/{plano.Id}", Ct);
        var planosDeB = await presidenteB.Cliente.GetFromJsonAsync<List<PlanoDeCobrancaResumoDTO>>($"{Rota}/planos", Json, Ct);
        var parcelasDeB = await presidenteB.Cliente.GetFromJsonAsync<PaginaDTO<ParcelaDTO>>($"{Rota}/parcelas", Json, Ct);
        var parcelasDeA = await presidenteA.Cliente.GetFromJsonAsync<PaginaDTO<ParcelaDTO>>($"{Rota}/parcelas", Json, Ct);

        planoPorB.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        planosDeB.ShouldBeEmpty();
        parcelasDeB!.Total.ShouldBe(0);
        parcelasDeA!.Total.ShouldBe(24);
    }

    private static async Task<PlanoDeCobrancaDTO> CriarPlano(HttpClient cliente, params ItemDeCobrancaRequestDTO[] itens)
    {
        var resposta = await cliente.PostAsJsonAsync($"{Rota}/planos", new PlanoDeCobrancaRequestDTO("Plano 2027", 200, 100, 3, 0), Json, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        var plano = (await resposta.Content.ReadFromJsonAsync<PlanoDeCobrancaDTO>(Json, Ct))!;

        foreach (var item in itens)
        {
            var inclusao = await cliente.PostAsJsonAsync($"{Rota}/planos/{plano.Id}/itens", item, Json, Ct);
            inclusao.StatusCode.ShouldBe(HttpStatusCode.OK);
            plano = (await inclusao.Content.ReadFromJsonAsync<PlanoDeCobrancaDTO>(Json, Ct))!;
        }

        return plano;
    }

    private static async Task Vigorar(HttpClient cliente, Guid planoId) =>
        (await cliente.PostAsync($"{Rota}/planos/{planoId}/vigorar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

    /// <summary>O que a adesão vai fazer: gerar e salvar, num contexto da turma.</summary>
    private async Task<int> GerarParcelas(Guid formaturaId, Guid usuarioId)
    {
        var vinculoId = await VinculoDe(formaturaId, usuarioId);

        await using var contexto = fabrica.ContextoDe(formaturaId);
        var plano = await new PlanoDeCobrancaRepository(contexto).ObterVigente(Ct);
        var resultado = await new GeracaoDeParcelasService(new ParcelaRepository(contexto)).Gerar(vinculoId, plano!, Ct);
        await new UnitOfWork(contexto).SalvarAsync(Ct);

        return resultado.Valor;
    }

    private async Task<Guid> VinculoDe(Guid formaturaId, Guid usuarioId)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Vinculos.Where(v => v.FormaturaId == formaturaId && v.UsuarioId == usuarioId).Select(v => v.Id).SingleAsync(Ct);
    }

    private async Task Suspender(Guid formaturaId)
    {
        await using var contexto = fabrica.ContextoDe(null);
        var formatura = await contexto.Formaturas.SingleAsync(f => f.Id == formaturaId, Ct);

        formatura.Transicionar(StatusDaFormatura.Suspensa).Sucesso.ShouldBeTrue();
        await contexto.SaveChangesAsync(Ct);
    }
}
