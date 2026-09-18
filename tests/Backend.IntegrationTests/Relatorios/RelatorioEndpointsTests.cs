using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend.Api.DTOs.Financeiro;
using Backend.Api.DTOs.Relatorios;
using Backend.Business.Common.Datas;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Relatorios.Models;
using Backend.IntegrationTests.Infra;
using ClosedXML.Excel;
using Shouldly;

namespace Backend.IntegrationTests.Relatorios;

/// <summary>
/// Os dois dashboards e os relatórios contra a API e o Postgres de verdade.
/// </summary>
/// <remarks>
/// É aqui que os critérios de aceite da Sprint 12 são cobrados: o painel público não devolve nome de
/// inadimplente em circunstância nenhuma, o de gestão recusa o formando, dashboard e balancete
/// mostram o mesmo saldo, e os quatro relatórios saem em .xlsx com número que soma.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class RelatorioEndpointsTests(ApiFactory fabrica)
{
    private const string Dashboard = "/api/v1/dashboard";
    private const string Relatorios = "/api/v1/relatorios";
    private const string Financeiro = "/api/v1/financeiro";
    private const string Despesas = $"{Financeiro}/despesas";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>O dia da aplicação — Brasília, como o validador de datas o entende, e não UTC.</summary>
    private static DateOnly Hoje => DataUtils.Hoje();

    /// <summary>
    /// O painel é de todo membro; os relatórios, não.
    /// </summary>
    /// <remarks>
    /// Os relatórios nomeiam quem deve — o balancete e as exportações respondem 403 para o formando.
    /// Política mal declarada não quebra o build — quebra aqui.
    /// <para>
    /// <c>/dashboard/gestao</c> não está mais na lista porque a rota deixou de existir (16/09/2026).
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task O_painel_e_de_todo_membro_e_os_relatorios_so_da_comissao(string papel, HttpStatusCode gestao)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        (await membro.Cliente.GetAsync($"{Dashboard}/publico", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await membro.Cliente.GetAsync($"{Relatorios}/balancete", Ct)).StatusCode.ShouldBe(gestao);
        (await membro.Cliente.GetAsync($"{Relatorios}/balancete.xlsx", Ct)).StatusCode.ShouldBe(gestao);
        (await membro.Cliente.GetAsync($"{Relatorios}/solicitacoes", Ct)).StatusCode.ShouldBe(gestao);
        (await membro.Cliente.PostAsJsonAsync($"{Relatorios}/solicitacoes", new SolicitarRelatorioDTO(), Json, Ct)).StatusCode.ShouldBe(gestao);
    }

    /// <summary>
    /// O corpo do painel público não tem nome de pessoa — e não tem como ter: o campo não existe no
    /// contrato, e o endpoint não consulta a lista.
    /// </summary>
    /// <remarks>
    /// Confere no JSON cru, e não no DTO desserializado: um campo que voltasse a mais apareceria aqui,
    /// mesmo que o tipo do teste o ignorasse em silêncio. É a checagem que sobrevive a uma refatoração
    /// que junte os dois payloads.
    /// </remarks>
    [Fact]
    public async Task O_painel_publico_nunca_traz_nome_de_inadimplente()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var tesoureiro = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Tesoureiro, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        await Lancar(tesoureiro, "Buffet do formando devedor", Ct);

        var corpo = await formando.Cliente.GetStringAsync($"{Dashboard}/publico", Ct);

        corpo.ShouldNotContain("inadimplentes", Case.Insensitive);
        corpo.ShouldNotContain("formandos_em_dia", Case.Insensitive);
        corpo.ShouldContain("adimplencia");
    }

    /// <summary>
    /// Dashboard e balancete do mesmo dia mostram o mesmo saldo.
    /// </summary>
    /// <remarks>
    /// É o teste que a decisão 4 existe para permitir: os dois números saem do mesmo
    /// <c>ICaixaService</c>. No dia em que alguém recalcular um dos dois, este teste cai.
    /// </remarks>
    [Fact]
    public async Task O_saldo_do_dashboard_e_o_do_balancete_sao_o_mesmo_numero()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);

        await Lancar(tesoureiro, "Buffet da comparação", Ct);

        var painel = (await tesoureiro.Cliente.GetFromJsonAsync<DashboardPublicoDTO>($"{Dashboard}/publico", Json, Ct))!;
        var balancete = (await tesoureiro.Cliente.GetFromJsonAsync<BalanceteDTO>($"{Relatorios}/balancete", Json, Ct))!;

        balancete.SaldoAcumuladoEmCentavos.ShouldBe(painel.Caixa.SaldoEmCentavos);
    }

    /// <summary>
    /// Os quatro relatórios saem em .xlsx de verdade, com o tipo certo e nome de arquivo.
    /// </summary>
    /// <remarks>
    /// Confere a assinatura do ZIP (<c>PK</c>), que é o que um XLSX é por dentro: uma resposta de erro
    /// ou um CSV rebatizado passariam por qualquer checagem só de cabeçalho.
    /// </remarks>
    [Theory]
    [InlineData("balancete")]
    [InlineData("despesas")]
    [InlineData("parcelas")]
    [InlineData("fornecedores")]
    public async Task Os_quatro_relatorios_saem_em_xlsx(string tipo)
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);

        await Lancar(tesoureiro, "Decoração com acentuação", Ct);

        var resposta = await tesoureiro.Cliente.GetAsync($"{Relatorios}/{tipo}.xlsx", Ct);
        var bytes = await resposta.Content.ReadAsByteArrayAsync(Ct);

        resposta.Content.Headers.ContentType?.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        resposta.Content.Headers.ContentDisposition?.FileName?.ShouldContain(tipo);

        bytes[0].ShouldBe((byte)'P');
        bytes[1].ShouldBe((byte)'K');
    }

    /// <summary>
    /// A planilha de despesas traz a linha lançada, com a acentuação intacta e o valor como número.
    /// </summary>
    /// <remarks>
    /// Dinheiro tem de chegar como número, e não como texto: coluna de texto não soma, e é isso que
    /// faz o contador redigitar a planilha inteira. Por isso o teste lê a célula, e não o XML cru.
    /// </remarks>
    [Fact]
    public async Task A_planilha_de_despesas_traz_a_linha_com_acentuacao_e_valor_que_soma()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);

        await Lancar(tesoureiro, "Decoração da colação", Ct);

        var bytes = await tesoureiro.Cliente.GetByteArrayAsync($"{Relatorios}/despesas.xlsx", Ct);

        using var memoria = new MemoryStream(bytes);
        using var livro = new XLWorkbook(memoria);
        var planilha = livro.Worksheets.First();

        planilha.Cell(1, 1).GetString().ShouldBe("Despesas lançadas");
        planilha.Cell(4, 3).GetString().ShouldBe("Descrição");
        planilha.Cell(5, 3).GetString().ShouldBe("Decoração da colação");
        planilha.Cell(5, 7).Value.IsNumber.ShouldBeTrue();
        planilha.Cell(5, 7).GetDouble().ShouldBe(1000d);
    }

    /// <summary>
    /// A solicitação volta na hora, na fila, e o clique duplo não vira dois pedidos.
    /// </summary>
    /// <remarks>
    /// O arquivo não é conferido aqui: quem o gera é o worker, que não sobe no teste de integração da
    /// API. O que se cobra é o contrato do endpoint — volta imediatamente e é idempotente.
    /// </remarks>
    [Fact]
    public async Task Solicitar_o_balancete_volta_na_hora_e_o_clique_duplo_nao_duplica()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var periodo = new SolicitarRelatorioDTO
        {
            Tipo = TipoDeRelatorio.Balancete,
            De = new DateOnly(Hoje.Year, 1, 1),
            Ate = Hoje,
        };

        var primeira = await Solicitar(tesoureiro, periodo);
        var segunda = await Solicitar(tesoureiro, periodo);

        primeira.Status.ShouldBe(StatusDaSolicitacao.NaFila);
        primeira.Disponivel.ShouldBeFalse();
        segunda.Id.ShouldBe(primeira.Id);

        var lista = (await tesoureiro.Cliente.GetFromJsonAsync<List<SolicitacaoDTO>>($"{Relatorios}/solicitacoes", Json, Ct))!;

        lista.Count(s => s.Id == primeira.Id).ShouldBe(1);
    }

    /// <summary>Solicitação na fila ainda não tem arquivo: o download responde 404, não um PDF vazio.</summary>
    [Fact]
    public async Task Baixar_uma_solicitacao_que_ainda_nao_ficou_pronta_responde_404()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);

        var solicitacao = await Solicitar(tesoureiro, new SolicitarRelatorioDTO());

        var resposta = await tesoureiro.Cliente.GetAsync($"{Relatorios}/solicitacoes/{solicitacao.Id}/arquivo", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Relatório de outra turma não existe: 404, e não 403.
    /// </summary>
    /// <remarks>
    /// Distinguir "não é seu" de "não existe" transformaria o endpoint num verificador de quais
    /// relatórios a turma vizinha pediu — a mesma regra do arquivo de terceiro.
    /// </remarks>
    [Fact]
    public async Task Solicitacao_de_outra_turma_responde_404()
    {
        var daPrimeira = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);
        var daSegunda = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);

        var solicitacao = await Solicitar(daPrimeira, new SolicitarRelatorioDTO());

        (await daSegunda.Cliente.GetAsync($"{Relatorios}/solicitacoes/{solicitacao.Id}/arquivo", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var lista = (await daSegunda.Cliente.GetFromJsonAsync<List<SolicitacaoDTO>>($"{Relatorios}/solicitacoes", Json, Ct))!;

        lista.ShouldNotContain(s => s.Id == solicitacao.Id);
    }

    /// <summary>Período invertido é aceito e trocado — a tela não precisa ordenar as datas por nós.</summary>
    [Fact]
    public async Task Periodo_invertido_na_query_volta_trocado_e_nao_com_400()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);

        var balancete = (await tesoureiro.Cliente.GetFromJsonAsync<BalanceteDTO>($"{Relatorios}/balancete?de=2026-09-01&ate=2026-03-01", Json, Ct))!;

        balancete.De.ShouldBe(new DateOnly(2026, 3, 1));
        balancete.Ate.ShouldBe(new DateOnly(2026, 9, 1));
    }

    private static async Task<SolicitacaoDTO> Solicitar(MembroDeTeste membro, SolicitarRelatorioDTO periodo)
    {
        var resposta = await membro.Cliente.PostAsJsonAsync($"{Relatorios}/solicitacoes", periodo, Json, Ct);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<SolicitacaoDTO>(Json, Ct))!;
    }

    /// <summary>Uma despesa paga de R$ 1.000, com comprovante — a saída que o balancete soma.</summary>
    /// <param name="membro">Quem lança.</param>
    /// <param name="descricao">Descrição da despesa.</param>
    /// <summary>
    /// A série mensal fecha com o total do período, e a comparação olha para o intervalo de antes.
    /// </summary>
    /// <remarks>
    /// É o que os indicadores e a curva da tela leem. Se a soma dos meses não bater com o total, os
    /// dois desenhos da mesma coisa discordam na mesma tela.
    /// </remarks>
    [Fact]
    public async Task O_balancete_traz_os_meses_do_periodo_e_os_totais_do_anterior()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);

        await Lancar(tesoureiro, "Decoração da colação", Ct);

        var dia = Hoje.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var balancete = (await tesoureiro.Cliente.GetFromJsonAsync<BalanceteDTO>($"{Relatorios}/balancete?de={dia}&ate={dia}", Json, Ct))!;

        balancete.Meses.ShouldHaveSingleItem().Mes.ShouldBe(new DateOnly(Hoje.Year, Hoje.Month, 1));
        balancete.Meses.Sum(mes => mes.SaidasEmCentavos).ShouldBe(balancete.SaidasEmCentavos);
        balancete.Meses.Sum(mes => mes.EntradasEmCentavos).ShouldBe(balancete.EntradasEmCentavos);

        // A véspera não tem lançamento nenhum: a turma nasceu agora.
        balancete.Anterior.SaidasEmCentavos.ShouldBe(0);
        balancete.Anterior.EntradasEmCentavos.ShouldBe(0);
        balancete.Anterior.ResultadoEmCentavos.ShouldBe(0);
    }

    /// <summary>
    /// Os seletores de filtro da tela carregam: o endpoint responde 200 e traz o fornecedor da turma.
    /// </summary>
    /// <remarks>
    /// Nasceu de um 500 em produção de desenvolvimento: a consulta dos formandos fazia
    /// <c>Distinct</c> sobre uma projeção com construtor e ordenava depois — LINQ que não traduz, e
    /// que só quebra na primeira chamada de verdade. Os combos vinham vazios na tela.
    /// </remarks>
    [Fact]
    public async Task Os_seletores_de_filtro_respondem_com_o_fornecedor_da_turma()
    {
        var tesoureiro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Tesoureiro, Ct);

        var criado = await tesoureiro.Cliente.PostAsJsonAsync(
            $"{Financeiro}/fornecedores",
            new FornecedorRequestDTO(
                "Buffet Sabor",
                "11.222.333/0001-81",
                CategoriaDeDespesa.Buffet,
                "(41) 99876-5432",
                "contato@fornecedor.dev",
                null,
                true
            ),
            Json,
            Ct
        );
        criado.StatusCode.ShouldBe(HttpStatusCode.Created);

        var opcoes = (await tesoureiro.Cliente.GetFromJsonAsync<OpcoesDeFiltroDTO>($"{Relatorios}/opcoes-de-filtro", Json, Ct))!;

        opcoes.Fornecedores.ShouldContain(o => o.Nome == "Buffet Sabor");
        opcoes.Formandos.ShouldNotBeNull();
        opcoes.Itens.ShouldNotBeNull();
    }

    private static async Task Lancar(MembroDeTeste membro, string descricao, CancellationToken ct)
    {
        var dia = Hoje.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var conteudo = new MultipartFormDataContent
        {
            { new StringContent(descricao), "descricao" },
            { new StringContent(nameof(CategoriaDeDespesa.Decoracao)), "categoria" },
            { new StringContent("100000"), "valorEmCentavos" },
            { new StringContent("1"), "numeroDeParcelas" },
            { new StringContent(dia), "competencia" },
            { new StringContent(dia), "vencimento" },
            { new StringContent(dia), "pagaEm" },
        };

        var comprovante = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4 comprovante"));
        comprovante.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        conteudo.Add(comprovante, "comprovante", "comprovante.pdf");

        (await membro.Cliente.PostAsync(Despesas, conteudo, ct)).EnsureSuccessStatusCode();
    }
}
