using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Relatorios;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Common.Datas;
using Backend.Business.Common.Planilhas;
using Backend.Business.Relatorios.Interfaces;
using Backend.Business.Relatorios.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Relatorios;

/// <summary>
/// O balancete do período e as exportações da turma.
/// </summary>
/// <remarks>
/// Tudo aqui é da Gestão. A exportação de parcelas nomeia quem deve, e as demais expõem o detalhe do
/// contrato com cada fornecedor — é material de trabalho da comissão, não prestação de contas.
/// <para>
/// A divisão de esforço é a da decisão 2: CSV sai na hora, em streaming; o PDF do balancete vira
/// solicitação, e quem gera é o worker.
/// </para>
/// </remarks>
/// <param name="relatorioService">Balancete, exportações e a fila.</param>
/// <param name="usuarioAtual">Quem chama — vai na capa do balancete e é dono da solicitação.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/relatorios")]
[Authorize(Policy = Politicas.Gestao)]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class RelatorioController(IRelatorioService relatorioService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>O balancete do período, consolidado — o que a tela desenha antes de exportar.</summary>
    /// <param name="de">Primeiro dia do período. Ausente, 1º de janeiro do ano do fim.</param>
    /// <param name="ate">Último dia do período. Ausente, hoje.</param>
    [HttpGet("balancete")]
    [ProducesResponseType(typeof(BalanceteDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Balancete(CancellationToken ct, DateOnly? de = null, DateOnly? ate = null) =>
        Responder(
            (await relatorioService.Balancete(FormaturaId, Periodo(de, ate), usuarioAtual.Id, ct)).Map(balancete => balancete.Adapt<BalanceteDTO>())
        );

    /// <summary>
    /// Um relatório em planilha do Excel (.xlsx), montado na própria requisição.
    /// </summary>
    /// <remarks>
    /// Substituiu o CSV: o arquivo chega com número que soma, data que ordena e o cabeçalho preso no
    /// topo, sem depender de o Excel adivinhar separador e codificação.
    /// <para>
    /// Síncrono, e o PDF não: montar algumas centenas de linhas num XLSX é instantâneo — o que demora
    /// é paginar e diagramar, e é por isso que só o PDF tem fila.
    /// </para>
    /// </remarks>
    /// <param name="tipo">Balancete, despesas, parcelas ou fornecedores.</param>
    /// <param name="recorte">Período e filtros, na query string. O balancete só lê o período.</param>
    [HttpGet("{tipo}.xlsx")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Exportar(TipoDeRelatorio tipo, [FromQuery] RecorteDoRelatorioDTO recorte, CancellationToken ct)
    {
        var filtro = Filtro(recorte);
        var tabela = await relatorioService.Tabela(FormaturaId, tipo, filtro, ct);

        if (tabela.Falhou)
            return Responder(tabela.Map(_ => 0));

        return File(
            PlanilhaExcel.Gerar(tabela.Valor),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{tipo.ToString().ToLowerInvariant()}-{filtro.Periodo.De:yyyy-MM-dd}-a-{filtro.Periodo.Ate:yyyy-MM-dd}.xlsx"
        );
    }

    /// <summary>Agenda o PDF de um relatório. Volta na hora; a tela acompanha a fila.</summary>
    /// <remarks>
    /// Pedido igual já na fila devolve o mesmo — clique duplo no botão não vira dois PDFs idênticos.
    /// </remarks>
    /// <param name="requisicao">Tipo e intervalo pedidos. Sem período, o ano corrente até hoje.</param>
    [HttpPost("solicitacoes")]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("relatorio.solicitado")]
    [ProducesResponseType(typeof(SolicitacaoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Solicitar([FromBody] SolicitarRelatorioDTO? requisicao, CancellationToken ct) =>
        Responder(
            (
                await relatorioService.Solicitar(
                    requisicao?.Tipo ?? TipoDeRelatorio.Balancete,
                    Filtro(requisicao ?? new SolicitarRelatorioDTO()),
                    usuarioAtual.Id,
                    ct
                )
            ).Map(solicitacao => solicitacao.Adapt<SolicitacaoDTO>())
        );

    /// <summary>
    /// O que os seletores de filtro da tela oferecem: fornecedores, formandos com parcela e itens.
    /// </summary>
    /// <remarks>
    /// Endpoint próprio, em vez de a tela consultar Fornecedores, Membros e Itens: o seletor quer id
    /// e nome, sem paginação nem o resto do cadastro, e são três consultas numa requisição.
    /// </remarks>
    [HttpGet("opcoes-de-filtro")]
    [ProducesResponseType(typeof(OpcoesDeFiltroDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> OpcoesDeFiltro(CancellationToken ct) =>
        Responder((await relatorioService.OpcoesDeFiltro(ct)).Map(opcoes => opcoes.Adapt<OpcoesDeFiltroDTO>()));

    /// <summary>As solicitações da turma, da mais recente — status e o que já pode ser baixado.</summary>
    [HttpGet("solicitacoes")]
    [ProducesResponseType(typeof(IReadOnlyList<SolicitacaoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarSolicitacoes(CancellationToken ct) =>
        Responder((await relatorioService.ListarSolicitacoes(ct)).Map(solicitacoes => solicitacoes.Adapt<IReadOnlyList<SolicitacaoDTO>>()));

    /// <summary>
    /// O PDF de uma solicitação pronta.
    /// </summary>
    /// <remarks>
    /// Solicitação de outra turma, ainda na fila ou já expirada respondem a mesma coisa: 404. Distinguir
    /// os casos transformaria o endpoint num verificador de quais relatórios a turma vizinha pediu.
    /// </remarks>
    /// <param name="id">Solicitação.</param>
    [HttpGet("solicitacoes/{id:guid}/arquivo")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BaixarPdf(Guid id, CancellationToken ct)
    {
        var resultado = await relatorioService.Baixar(id, usuarioAtual.Id, ct);

        if (resultado.Falhou)
            return Responder(resultado.Map(_ => 0));

        return File(resultado.Valor.Conteudo, resultado.Valor.ContentType, resultado.Valor.Nome);
    }

    /// <summary>O período pedido, com as pontas que faltam completadas.</summary>
    /// <param name="de">Começo pedido.</param>
    /// <param name="ate">Fim pedido.</param>
    private static PeriodoDoRelatorio Periodo(DateOnly? de, DateOnly? ate) => PeriodoDoRelatorio.Normalizar(de, ate, DataUtils.Hoje());

    /// <summary>O recorte pedido como o Business o lê — o mesmo caminho para a planilha e para o PDF.</summary>
    /// <param name="recorte">Período e filtros como vieram da borda.</param>
    private static FiltroDoRelatorio Filtro(RecorteDoRelatorioDTO recorte) => recorte.ParaFiltro(DataUtils.Hoje());
}
