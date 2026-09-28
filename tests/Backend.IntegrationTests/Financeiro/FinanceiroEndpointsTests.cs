using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Api.DTOs.Financeiro;
using Backend.Business.Common.Datas;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Mvc;
using Shouldly;

namespace Backend.IntegrationTests.Financeiro;

/// <summary>
/// Despesas, fornecedores e caixa contra a API e o Postgres de verdade: quem pode chamar cada
/// endpoint e os critérios de aceite da Sprint 10 — parcelada em 3× gera 3 linhas, lançar duas vezes
/// cria uma só, pagar sem comprovante é 400, excluir fornecedor em uso é 409 e o saldo é agregação.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class FinanceiroEndpointsTests(ApiFactory fabrica)
{
    private const string Fornecedores = "/api/v1/financeiro/fornecedores";
    private const string Despesas = "/api/v1/financeiro/despesas";
    private const string Caixa = "/api/v1/financeiro/caixa";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>O dia da aplicação — Brasília, como o validador de datas o entende, e não UTC.</summary>
    /// <remarks>
    /// Com UTC, todo teste que lança uma despesa paga hoje falhava entre 21h e meia-noite de
    /// Brasília: para o validador a data estava no futuro, e o 400 vinha por outro motivo que não o
    /// esperado.
    /// </remarks>
    private static DateOnly Hoje => DataUtils.Hoje();

    /// <summary>O que a turma ainda deve, pela faixa da tela de Despesas.</summary>
    /// <remarks>
    /// O caixa não devolve mais esse total em campo próprio: ele entra no saldo projetado, e o número
    /// detalhado é da tela que sabe abri-lo em linhas.
    /// </remarks>
    /// <param name="membro">Quem consulta.</param>
    private static async Task<long> APagar(MembroDeTeste membro) =>
        (await membro.Cliente.GetFromJsonAsync<ResumoDeDespesasDTO>($"{Despesas}/resumo", Json, Ct))!.Prevista.ValorEmCentavos;

    /// <summary>
    /// A prestação de contas é de todo membro; o cadastro de fornecedor e a projeção, não.
    /// </summary>
    /// <remarks>
    /// O caixa e a lista de despesas dizem no que a turma gastou, e quem paga a turma tem direito de
    /// ler — são somas e contratos, sem nome de ninguém. Fornecedor é cadastro de trabalho da
    /// Tesouraria, e a projeção é planejamento da Gestão.
    /// </remarks>
    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task Fornecedor_e_projecao_sao_da_gestao_mas_o_caixa_e_de_todo_membro(string papel, HttpStatusCode tesouraria, HttpStatusCode gestao)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        (await membro.Cliente.GetAsync(Fornecedores, Ct)).StatusCode.ShouldBe(tesouraria);
        (await membro.Cliente.GetAsync($"{Fornecedores}/resumo", Ct)).StatusCode.ShouldBe(tesouraria);
        (await membro.Cliente.GetAsync($"{Caixa}/projecao", Ct)).StatusCode.ShouldBe(gestao);

        (await membro.Cliente.GetAsync(Despesas, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await membro.Cliente.GetAsync($"{Despesas}/resumo", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await membro.Cliente.GetAsync(Caixa, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await membro.Cliente.GetAsync($"{Caixa}/arrecadacao", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Formando_le_a_despesa_mas_nao_lanca_paga_nem_abre_comprovante()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);
        var despesa = (await Lancar(tesoureiro, Nova("Buffet"), Ct))[0];
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        (await formando.Cliente.PostAsync(Despesas, Multipart(Nova("Banda")), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.Cliente.PostAsync($"{Despesas}/{despesa.Id}/pagar", Pagamento(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.Cliente.PostAsync($"{Despesas}/{despesa.Id}/cancelar", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.Cliente.PostAsJsonAsync(Fornecedores, Fornecedor("Banda Boa"), Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Ler ele lê: é a prestação de contas da turma. O comprovante é que não — o anexo traz conta
        // e titular do fornecedor, e por isso segue da Tesouraria.
        (await formando.Cliente.GetAsync($"{Despesas}/{despesa.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await formando.Cliente.GetAsync($"{Despesas}/{despesa.Id}/comprovante", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Parcelada_em_3x_gera_3_linhas_com_vencimentos_mensais()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var vencimento = new DateOnly(Hoje.Year + 1, 1, 31);

        var linhas = await Lancar(tesoureiro, Nova("Buffet") with { ValorEmCentavos = 100_000, NumeroDeParcelas = 3, Vencimento = vencimento }, Ct);

        linhas.Count.ShouldBe(3);
        linhas.Sum(linha => linha.ValorEmCentavos).ShouldBe(100_000);
        linhas.Select(linha => linha.Vencimento).ShouldBe([vencimento, new DateOnly(vencimento.Year, 2, 28), new DateOnly(vencimento.Year, 3, 31)]);
        linhas.Select(linha => $"{linha.Numero}/{linha.TotalDeParcelas}").ShouldBe(["1/3", "2/3", "3/3"]);
    }

    [Fact]
    public async Task Lancar_a_mesma_despesa_duas_vezes_cria_uma_so()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var dados = Nova("Aluguel do salão");

        await Lancar(tesoureiro, dados, Ct);
        var segunda = await tesoureiro.Cliente.PostAsync(Despesas, Multipart(dados), Ct);

        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await segunda.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct))!.Extensions["codigo"]!
            .ToString()
            .ShouldBe("financeiro.despesa_duplicada");

        var lista = await tesoureiro.Cliente.GetFromJsonAsync<PaginaDeDespesas>($"{Despesas}?busca=Aluguel", Json, Ct);
        lista!.Total.ShouldBe(1);
    }

    [Fact]
    public async Task Sem_fornecedor_a_duplicidade_tambem_e_barrada()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var dados = Nova("Taxa bancária") with { Categoria = CategoriaDeDespesa.Taxas };

        await Lancar(tesoureiro, dados, Ct);

        (await tesoureiro.Cliente.PostAsync(Despesas, Multipart(dados), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Pagar_sem_comprovante_devolve_400_e_a_despesa_continua_prevista()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var despesa = (await Lancar(tesoureiro, Nova("Fotografia"), Ct))[0];

        var resposta = await tesoureiro.Cliente.PostAsync($"{Despesas}/{despesa.Id}/pagar", Pagamento(comprovante: false), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problema = await resposta.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, Ct);
        problema!.Errors["comprovante"].ShouldHaveSingleItem().ShouldContain("comprovante");

        var depois = await tesoureiro.Cliente.GetFromJsonAsync<DespesaDTO>($"{Despesas}/{despesa.Id}", Json, Ct);
        depois!.Status.ShouldBe(StatusDaDespesa.Prevista);
    }

    [Fact]
    public async Task Pagar_com_comprovante_muda_o_saldo_e_o_comprovante_abre()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var despesa = (await Lancar(tesoureiro, Nova("Decoração") with { ValorEmCentavos = 250_000 }, Ct))[0];

        var antes = await tesoureiro.Cliente.GetFromJsonAsync<CaixaDTO>(Caixa, Json, Ct);
        antes!.GastoEmCentavos.ShouldBe(0);
        (await APagar(tesoureiro)).ShouldBe(250_000);

        (await tesoureiro.Cliente.PostAsync($"{Despesas}/{despesa.Id}/pagar", Pagamento(), Ct)).EnsureSuccessStatusCode();

        var depois = await tesoureiro.Cliente.GetFromJsonAsync<CaixaDTO>(Caixa, Json, Ct);
        depois!.GastoEmCentavos.ShouldBe(250_000);
        depois.SaldoEmCentavos.ShouldBe(-250_000);
        (await APagar(tesoureiro)).ShouldBe(0);
        depois.PorCategoria.ShouldHaveSingleItem().PagoEmCentavos.ShouldBe(250_000);
        depois.Ultimos.ShouldHaveSingleItem().Entrada.ShouldBeFalse();

        var comprovante = await tesoureiro.Cliente.GetAsync($"{Despesas}/{despesa.Id}/comprovante", Ct);
        comprovante.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Despesa_prevista_nao_entra_no_gasto_mas_entra_no_a_pagar()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);

        await Lancar(tesoureiro, Nova("Convites") with { ValorEmCentavos = 120_000, NumeroDeParcelas = 2 }, Ct);

        var caixa = await tesoureiro.Cliente.GetFromJsonAsync<CaixaDTO>(Caixa, Json, Ct);
        caixa!.GastoEmCentavos.ShouldBe(0);
        caixa.SaldoEmCentavos.ShouldBe(0);
        (await APagar(tesoureiro)).ShouldBe(120_000);
    }

    [Fact]
    public async Task Cancelar_tira_a_despesa_do_a_pagar_e_libera_o_relancamento()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var dados = Nova("Banda") with { ValorEmCentavos = 300_000 };
        var despesa = (await Lancar(tesoureiro, dados, Ct))[0];

        (await tesoureiro.Cliente.PostAsync($"{Despesas}/{despesa.Id}/cancelar", null, Ct)).EnsureSuccessStatusCode();

        var caixa = await tesoureiro.Cliente.GetFromJsonAsync<CaixaDTO>(Caixa, Json, Ct);
        (await APagar(tesoureiro)).ShouldBe(0);

        (await tesoureiro.Cliente.PostAsync(Despesas, Multipart(dados), Ct)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Fornecedor_com_despesa_nao_e_excluido_mas_e_desativado()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var criado = await tesoureiro.Cliente.PostAsJsonAsync(Fornecedores, Fornecedor("Buffet Sabor"), Json, Ct);
        criado.StatusCode.ShouldBe(HttpStatusCode.Created);
        var fornecedor = (await criado.Content.ReadFromJsonAsync<FornecedorDTO>(Json, Ct))!;

        await Lancar(tesoureiro, Nova("Buffet") with { FornecedorId = fornecedor.Id }, Ct);

        var exclusao = await tesoureiro.Cliente.DeleteAsync($"{Fornecedores}/{fornecedor.Id}", Ct);
        exclusao.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await exclusao.Content.ReadFromJsonAsync<ProblemDetails>(Json, Ct))!.Extensions["codigo"]!
            .ToString()
            .ShouldBe("financeiro.fornecedor_em_uso");

        var desativacao = await tesoureiro.Cliente.PutAsJsonAsync(
            $"{Fornecedores}/{fornecedor.Id}",
            Fornecedor("Buffet Sabor") with
            {
                Ativo = false,
            },
            Json,
            Ct
        );
        desativacao.EnsureSuccessStatusCode();
        (await desativacao.Content.ReadFromJsonAsync<FornecedorDTO>(Json, Ct))!.Ativo.ShouldBeFalse();

        var resumo = await tesoureiro.Cliente.GetFromJsonAsync<ContagemDeFornecedoresDTO>($"{Fornecedores}/resumo", Json, Ct);
        resumo.ShouldBe(new ContagemDeFornecedoresDTO(0, 1));
    }

    [Fact]
    public async Task Fornecedor_sem_despesa_e_excluido_e_o_nome_repetido_e_conflito()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var criado = await tesoureiro.Cliente.PostAsJsonAsync(Fornecedores, Fornecedor("Gráfica Rápida"), Json, Ct);
        var fornecedor = (await criado.Content.ReadFromJsonAsync<FornecedorDTO>(Json, Ct))!;

        var repetido = await tesoureiro.Cliente.PostAsJsonAsync(Fornecedores, Fornecedor("gráfica rápida"), Json, Ct);
        repetido.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await tesoureiro.Cliente.DeleteAsync($"{Fornecedores}/{fornecedor.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await tesoureiro.Cliente.GetAsync($"{Fornecedores}/{fornecedor.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Despesa_de_outra_turma_nao_existe_aqui()
    {
        var daOutra = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var despesa = (await Lancar(daOutra, Nova("Espaço"), Ct))[0];
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);

        (await tesoureiro.Cliente.GetAsync($"{Despesas}/{despesa.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await tesoureiro.Cliente.PostAsync($"{Despesas}/{despesa.Id}/pagar", Pagamento(), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await APagar(tesoureiro)).ShouldBe(0);
    }

    [Fact]
    public async Task Projecao_poe_a_despesa_atrasada_no_mes_atual()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var mesPassado = Hoje.AddMonths(-1);

        await Lancar(tesoureiro, Nova("Beca") with { ValorEmCentavos = 80_000, Vencimento = mesPassado, Competencia = mesPassado }, Ct);

        var projecao = await tesoureiro.Cliente.GetFromJsonAsync<ProjecaoDTO>($"{Caixa}/projecao", Json, Ct);
        var mesAtual = projecao!.Meses.Single(mes => mes.Mes == new DateOnly(Hoje.Year, Hoje.Month, 1));

        mesAtual.SaidasPrevistasEmCentavos.ShouldBe(80_000);
        projecao.Meses.ShouldAllBe(mes => mes.Mes >= new DateOnly(Hoje.Year, Hoje.Month, 1));
    }

    [Fact]
    public async Task Turma_suspensa_le_o_caixa_mas_nao_lanca_despesa()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Suspensa, Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);

        (await tesoureiro.Cliente.GetAsync(Caixa, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await tesoureiro.Cliente.GetAsync(Despesas, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await tesoureiro.Cliente.PostAsync(Despesas, Multipart(Nova("Buffet")), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Nao_existe_coluna_de_saldo_no_banco()
    {
        await using var contexto = fabrica.ContextoDe(null);

        var colunas = contexto.Model.FindEntityType(typeof(Despesa))!.GetProperties().Select(propriedade => propriedade.Name).ToList();

        colunas.ShouldNotContain(nome => nome.Contains("Saldo", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<IReadOnlyList<DespesaDTO>> Lancar(MembroDeTeste membro, NovaDespesaRequestDTO dados, CancellationToken ct)
    {
        var resposta = await membro.Cliente.PostAsync(Despesas, Multipart(dados), ct);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<List<DespesaDTO>>(Json, Ct))!;
    }

    private static NovaDespesaRequestDTO Nova(string descricao) =>
        new(null, null, descricao, CategoriaDeDespesa.Buffet, 100_000, 1, Hoje, Hoje, null);

    /// <summary>O lançamento como multipart — é assim que o formulário da tela o envia.</summary>
    private static MultipartFormDataContent Multipart(NovaDespesaRequestDTO dados)
    {
        var conteudo = new MultipartFormDataContent
        {
            { new StringContent(dados.Descricao!), "descricao" },
            { new StringContent(dados.Categoria.ToString()), "categoria" },
            { new StringContent(dados.ValorEmCentavos.ToString(CultureInfo.InvariantCulture)), "valorEmCentavos" },
            { new StringContent(dados.NumeroDeParcelas.ToString(CultureInfo.InvariantCulture)), "numeroDeParcelas" },
            { new StringContent(dados.Competencia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "competencia" },
            { new StringContent(dados.Vencimento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "vencimento" },
        };

        if (dados.FornecedorId is { } fornecedorId)
            conteudo.Add(new StringContent(fornecedorId.ToString()), "fornecedorId");

        if (dados.ItemDaFestaId is { } itemDaFestaId)
            conteudo.Add(new StringContent(itemDaFestaId.ToString()), "itemDaFestaId");

        if (dados.PagaEm is { } pagaEm)
        {
            conteudo.Add(new StringContent(pagaEm.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "pagaEm");
            conteudo.Add(Arquivo(), "comprovante", "comprovante.pdf");
        }

        return conteudo;
    }

    private static MultipartFormDataContent Pagamento(bool comprovante = true)
    {
        var conteudo = new MultipartFormDataContent { { new StringContent(Hoje.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "pagoEm" } };

        if (comprovante)
            conteudo.Add(Arquivo(), "comprovante", "comprovante.pdf");

        return conteudo;
    }

    private static ByteArrayContent Arquivo()
    {
        var arquivo = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4 comprovante de teste"));
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        return arquivo;
    }

    private static FornecedorRequestDTO Fornecedor(string nome) =>
        new(nome, "11.222.333/0001-81", CategoriaDeDespesa.Buffet, "(41) 99876-5432", "contato@fornecedor.dev", null, true);

    /// <summary>Página de despesas como a API a devolve — só o que o teste lê.</summary>
    /// <param name="Itens">Despesas da página.</param>
    /// <param name="Total">Total no filtro.</param>
    private sealed record PaginaDeDespesas(IReadOnlyList<DespesaDTO> Itens, long Total);
}
