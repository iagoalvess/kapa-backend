using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend.Api.DTOs.Comunicacao;
using Backend.Api.DTOs.Festa;
using Backend.Api.DTOs.Financeiro;
using Backend.Api.DTOs.Relatorios;
using Backend.Business.Common.Datas;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Mvc;
using Shouldly;

namespace Backend.IntegrationTests.Financeiro;

/// <summary>
/// Receitas da Sprint 28 contra a API e o Postgres de verdade: quem pode lançar, e o critério de
/// aceite de cada consulta de dinheiro da decisão 3 — caixa, extrato, quadro, projeção, dashboard,
/// meta da festa e balancete —, com a receita prevista presa à projeção (P2).
/// </summary>
/// <remarks>
/// As somas do caixa são <c>UNION ALL</c> traduzidos pelo EF: é aqui, e não no teste unitário, que
/// uma operação de conjunto que o provedor não traduz aparece.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class OutraReceitaEndpointsTests(ApiFactory fabrica)
{
    private const string OutrasReceitas = "/api/v1/financeiro/outras-receitas";
    private const string Caixa = "/api/v1/financeiro/caixa";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Hoje => DataUtils.Hoje();

    /// <summary>Ler é de todo membro, como a despesa; lançar é da Tesouraria (P1).</summary>
    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.Created)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.Created)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Todo_membro_le_e_so_a_tesouraria_lanca(string papel, HttpStatusCode lancar)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        (await membro.Cliente.GetAsync(OutrasReceitas, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await membro.Cliente.GetAsync($"{OutrasReceitas}/resumo", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await membro.Cliente.PostAsJsonAsync(OutrasReceitas, Nova("Cota ouro"), Json, Ct)).StatusCode.ShouldBe(lancar);
    }

    /// <summary>
    /// Recebida sobe o arrecadado, o saldo, o extrato, o quadro, o mês do gráfico e a meta da festa —
    /// sem ninguém editar mais nada.
    /// </summary>
    [Fact]
    public async Task OutraReceita_recebida_entra_em_toda_consulta_de_dinheiro()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);

        await Lancar(tesoureiro, Nova("Cota ouro do palco") with { ValorEmCentavos = 5_000_00, Recebida = true });
        await Lancar(
            tesoureiro,
            Nova("Rendimento de setembro", CategoriaDeOutraReceita.Rendimento) with
            {
                ValorEmCentavos = 120_00,
                Recebida = true,
            }
        );

        var caixa = await Obter<CaixaDTO>(tesoureiro, Caixa);
        caixa.ArrecadadoEmCentavos.ShouldBe(5_120_00);
        caixa.SaldoEmCentavos.ShouldBe(5_120_00);
        caixa.Ultimos.Where(l => l.Entrada).Select(l => l.Descricao).ShouldBe(["Cota ouro do palco", "Rendimento de setembro"], ignoreOrder: true);
        caixa
            .OutrasReceitasPorCategoria.Select(c => (c.Categoria, c.RecebidoEmCentavos))
            .ShouldBe([(CategoriaDeOutraReceita.Patrocinio, 5_000_00L), (CategoriaDeOutraReceita.Rendimento, 120_00L)]);

        var projecao = await Obter<ProjecaoDTO>(tesoureiro, $"{Caixa}/projecao");
        projecao.Meses.Single(m => m.Mes == PrimeiroDoMes(Hoje)).EntradasEmCentavos.ShouldBe(5_120_00);

        (await Obter<IReadOnlyList<MesDaArrecadacaoDTO>>(tesoureiro, $"{Caixa}/arrecadacao"))[^2].ArrecadadoEmCentavos.ShouldBe(5_120_00);
        (await Obter<DashboardPublicoDTO>(tesoureiro, "/api/v1/dashboard/publico")).Caixa.ArrecadadoEmCentavos.ShouldBe(5_120_00);
        (await Obter<MetaDaFestaDTO>(tesoureiro, "/api/v1/festa/meta")).ArrecadadoEmCentavos.ShouldBe(5_120_00);
    }

    /// <summary>A prevista só aparece na projeção do caixa: nem arrecadado, nem Início, nem meta (P2).</summary>
    [Fact]
    public async Task OutraReceita_prevista_so_entra_na_projecao()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var proximoMes = PrimeiroDoMes(Hoje).AddMonths(1);

        await Lancar(tesoureiro, Nova("Patrocínio prometido") with { ValorEmCentavos = 9_000_00, Data = proximoMes.AddDays(9) });

        (await Obter<CaixaDTO>(tesoureiro, Caixa)).ArrecadadoEmCentavos.ShouldBe(0);
        (await Obter<MetaDaFestaDTO>(tesoureiro, "/api/v1/festa/meta")).ArrecadadoEmCentavos.ShouldBe(0);
        (await Obter<IReadOnlyList<MesDaArrecadacaoDTO>>(tesoureiro, $"{Caixa}/arrecadacao"))[^1].ArrecadadoEmCentavos.ShouldBe(0);

        var projecao = await Obter<ProjecaoDTO>(tesoureiro, $"{Caixa}/projecao");
        projecao.Meses.Single(m => m.Mes == proximoMes).EntradasPrevistasEmCentavos.ShouldBe(9_000_00);
        projecao.SaldoEmCentavos.ShouldBe(0);
    }

    [Fact]
    public async Task Receber_leva_ao_arrecadado_e_a_segunda_vez_e_conflito()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var outraReceita = await Lancar(
            tesoureiro,
            Nova("Festa junina") with
            {
                Categoria = CategoriaDeOutraReceita.Evento,
                Data = Hoje.AddDays(20),
            }
        );

        var recebida = await tesoureiro.Cliente.PostAsJsonAsync(
            $"{OutrasReceitas}/{outraReceita.Id}/receber",
            new ReceberOutraReceitaRequestDTO(Hoje),
            Json,
            Ct
        );
        recebida.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await recebida.Content.ReadFromJsonAsync<OutraReceitaDTO>(Json, Ct))!.Data.ShouldBe(Hoje);
        (await Obter<CaixaDTO>(tesoureiro, Caixa)).ArrecadadoEmCentavos.ShouldBe(Nova("x").ValorEmCentavos);

        await Codigo(
            await tesoureiro.Cliente.PostAsJsonAsync(
                $"{OutrasReceitas}/{outraReceita.Id}/receber",
                new ReceberOutraReceitaRequestDTO(Hoje),
                Json,
                Ct
            ),
            HttpStatusCode.Conflict,
            "financeiro.outra_receita_ja_recebida"
        );
        await Codigo(
            await tesoureiro.Cliente.PostAsync($"{OutrasReceitas}/{outraReceita.Id}/cancelar", null, Ct),
            HttpStatusCode.Conflict,
            "financeiro.outra_receita_ja_recebida"
        );
    }

    [Fact]
    public async Task Cancelar_a_prevista_tira_da_projecao_e_libera_o_relancamento()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var dados = Nova("Bingo") with { Categoria = CategoriaDeOutraReceita.Evento, Origem = null, Data = Hoje.AddDays(3) };
        var outraReceita = await Lancar(tesoureiro, dados);

        await Codigo(
            await tesoureiro.Cliente.PostAsJsonAsync(OutrasReceitas, dados, Json, Ct),
            HttpStatusCode.Conflict,
            "financeiro.outra_receita_duplicada"
        );

        (await tesoureiro.Cliente.PostAsync($"{OutrasReceitas}/{outraReceita.Id}/cancelar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Obter<ProjecaoDTO>(tesoureiro, $"{Caixa}/projecao")).Meses.Sum(m => m.EntradasPrevistasEmCentavos).ShouldBe(0);

        (await tesoureiro.Cliente.PostAsJsonAsync(OutrasReceitas, dados, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary>O balancete traz as receitas em seção própria, e a soma das entradas bate com o caixa.</summary>
    [Fact]
    public async Task Balancete_tem_secao_de_outras_receitas_e_fecha_com_o_caixa()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        await Lancar(tesoureiro, Nova("Cota prata") with { ValorEmCentavos = 2_000_00, Recebida = true });
        await Lancar(tesoureiro, Nova("Doação da família", CategoriaDeOutraReceita.Doacao) with { ValorEmCentavos = 300_00, Recebida = true });
        await Lancar(tesoureiro, Nova("Cota bronze") with { ValorEmCentavos = 1_000_00, Data = Hoje.AddMonths(1) });

        var dia = Hoje.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var balancete = await Obter<BalanceteDTO>(tesoureiro, $"/api/v1/relatorios/balancete?de={dia}&ate={dia}");

        balancete.OutrasReceitas.Select(l => l.Rotulo).ShouldBe(["Patrocínio", "Doação"]);
        balancete.EntradasEmCentavos.ShouldBe(2_300_00);
        balancete.Meses.Sum(m => m.EntradasEmCentavos).ShouldBe(2_300_00);
        balancete.EntradasEmCentavos.ShouldBe((await Obter<CaixaDTO>(tesoureiro, Caixa)).ArrecadadoEmCentavos);

        (await tesoureiro.Cliente.GetAsync($"/api/v1/relatorios/balancete.xlsx?de={dia}&ate={dia}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>O comprovante é do acervo e visível à turma — o formando abre o mesmo documento.</summary>
    [Fact]
    public async Task Comprovante_do_acervo_so_se_for_visivel_para_a_turma()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var daTurma = await EnviarDocumento(presidente, "Contrato de patrocínio", Visibilidade.Turma);
        var daComissao = await EnviarDocumento(presidente, "Negociação interna", Visibilidade.SomenteComissao);

        await Codigo(
            await presidente.Cliente.PostAsJsonAsync(OutrasReceitas, Nova("Cota ouro") with { DocumentoId = daComissao }, Json, Ct),
            HttpStatusCode.BadRequest,
            "financeiro.documento_nao_encontrado"
        );

        var outraReceita = await Lancar(presidente, Nova("Cota ouro") with { DocumentoId = daTurma, Recebida = true });
        outraReceita.Documento!.Titulo.ShouldBe("Contrato de patrocínio");

        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        (await Obter<OutraReceitaDTO>(formando, $"{OutrasReceitas}/{outraReceita.Id}")).Documento!.Id.ShouldBe(daTurma);
        (await formando.Cliente.GetAsync($"/api/v1/comunicacao/documentos/{daTurma}/download", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OutraReceita_de_outra_turma_nao_existe_aqui()
    {
        var dona = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var outraReceita = await Lancar(dona, Nova("Cota ouro") with { Recebida = true });
        var outra = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);

        (await outra.Cliente.GetAsync($"{OutrasReceitas}/{outraReceita.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outra.Cliente.PostAsync($"{OutrasReceitas}/{outraReceita.Id}/cancelar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Obter<CaixaDTO>(outra, Caixa)).ArrecadadoEmCentavos.ShouldBe(0);
    }

    private static NovaOutraReceitaRequestDTO Nova(string descricao, CategoriaDeOutraReceita categoria = CategoriaDeOutraReceita.Patrocinio) =>
        new(descricao, "Clínica Sorriso", categoria, 1_500_00, Hoje, false, null);

    private static DateOnly PrimeiroDoMes(DateOnly dia) => new(dia.Year, dia.Month, 1);

    private static async Task<OutraReceitaDTO> Lancar(MembroDeTeste membro, NovaOutraReceitaRequestDTO dados)
    {
        var resposta = await membro.Cliente.PostAsJsonAsync(OutrasReceitas, dados, Json, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created, await resposta.Content.ReadAsStringAsync(Ct));

        return (await resposta.Content.ReadFromJsonAsync<OutraReceitaDTO>(Json, Ct))!;
    }

    private static async Task<T> Obter<T>(MembroDeTeste membro, string rota) => (await membro.Cliente.GetFromJsonAsync<T>(rota, Json, Ct))!;

    private static async Task Codigo(HttpResponseMessage resposta, HttpStatusCode status, string codigo)
    {
        resposta.StatusCode.ShouldBe(status);
        (await resposta.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct))!.Extensions["codigo"]!.ToString().ShouldBe(codigo);
    }

    /// <summary>Sobe um documento ao acervo e devolve o id — o mesmo caminho da tela Documentos.</summary>
    private static async Task<Guid> EnviarDocumento(MembroDeTeste membro, string titulo, Visibilidade visibilidade)
    {
        var arquivo = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4 comprovante de teste"));
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        var corpo = new MultipartFormDataContent
        {
            { new StringContent(titulo), "titulo" },
            { new StringContent(nameof(CategoriaDeDocumento.Contrato)), "categoria" },
            { new StringContent(visibilidade.ToString()), "visibilidade" },
        };
        corpo.Add(arquivo, "arquivo", "comprovante.pdf");

        var resposta = await membro.Cliente.PostAsync("/api/v1/comunicacao/documentos", corpo, Ct);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<DocumentoDTO>(Json, Ct))!.Id;
    }
}
