using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Financeiro;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Financeiro;

/// <summary>
/// O que a turma deve e o que ela já pagou.
/// </summary>
/// <remarks>
/// Escrever é da Tesouraria, e exige a turma ativa. Pagar é multipart: sem comprovante, 400
/// <c>financeiro.comprovante_obrigatorio</c> (decisão 3).
/// <para>
/// <b>Ler é de todo membro, o formando inclusive</b>: é nesta lista que se vê no que a turma gastou o
/// dinheiro dela, e prestação de contas que só a comissão enxerga não presta contas a ninguém. O
/// comprovante é a exceção — segue da Tesouraria, porque o anexo costuma trazer conta e titular.
/// </para>
/// </remarks>
/// <param name="despesaService">Regras da despesa.</param>
/// <param name="usuarioAtual">Quem chama — dono do comprovante enviado.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Despesas)]
[Route("api/v{version:apiVersion}/financeiro/despesas")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class DespesaController(IDespesaService despesaService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>As despesas da turma, por vencimento.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="filtro">Lançamento, fornecedor, categoria, situação, período e busca.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(PaginaDTO<DespesaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar([FromQuery] PaginacaoRequestDTO paginacao, [FromQuery] FiltroDeDespesas filtro, CancellationToken ct)
    {
        var resultado = await despesaService.Listar(paginacao.ParaModelo(), filtro, ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(despesa => despesa.Adapt<DespesaDTO>())));
    }

    /// <summary>Quantas e quanto, por situação, dentro do mesmo filtro — a faixa da tela, numa consulta.</summary>
    /// <param name="filtro">Mesmos filtros da lista; a situação é ignorada.</param>
    [HttpGet("resumo")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(ResumoDeDespesasDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resumir([FromQuery] FiltroDeDespesas filtro, CancellationToken ct) =>
        Responder((await despesaService.Resumir(filtro, ct)).Map(resumo => resumo.Adapt<ResumoDeDespesasDTO>()));

    /// <summary>Uma despesa da turma.</summary>
    /// <param name="id">Despesa.</param>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(DespesaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct) =>
        Responder((await despesaService.ObterPorId(id, ct)).Map(despesa => despesa.Adapt<DespesaDTO>()));

    /// <summary>Lança a despesa: uma linha à vista, N linhas mensais na parcelada.</summary>
    /// <remarks>
    /// Multipart: o comprovante só é exigido quando <c>pagaEm</c> vem preenchido. 409
    /// <c>financeiro.despesa_duplicada</c> no clique repetido.
    /// </remarks>
    /// <param name="requisicao">Fornecedor, descrição, valor total, parcelas e datas.</param>
    /// <param name="comprovante">PDF ou imagem; obrigatório quando a despesa já nasce paga.</param>
    [HttpPost]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [Consumes("multipart/form-data")]
    [RegistrarEvento("financeiro.despesa_lancada")]
    [ProducesResponseType(typeof(IReadOnlyList<DespesaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Lancar([FromForm] NovaDespesaRequestDTO requisicao, IFormFile? comprovante, CancellationToken ct)
    {
        await using var conteudo = comprovante?.OpenReadStream() ?? Stream.Null;

        var dados = new NovaDespesa(
            requisicao.FornecedorId,
            requisicao.ItemDaFestaId,
            requisicao.Descricao ?? string.Empty,
            requisicao.Categoria,
            requisicao.ValorEmCentavos,
            requisicao.NumeroDeParcelas,
            requisicao.Competencia,
            requisicao.Vencimento,
            requisicao.PagaEm
        );

        var resultado = await despesaService.Lancar(dados, usuarioAtual.Id, comprovante.ParaNovoArquivo(conteudo), ct);

        return Responder(resultado.Map(despesas => despesas.Adapt<List<DespesaDTO>>()));
    }

    /// <summary>Corrige uma linha lançada — prevista ou paga (decisão 5).</summary>
    /// <param name="id">Despesa.</param>
    /// <param name="requisicao">Dados novos.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("financeiro.despesa_alterada", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(DespesaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarDespesaRequestDTO requisicao, CancellationToken ct)
    {
        var dados = new DadosDaDespesa(
            requisicao.FornecedorId,
            requisicao.ItemDaFestaId,
            requisicao.Descricao ?? string.Empty,
            requisicao.Categoria,
            requisicao.ValorEmCentavos,
            requisicao.Competencia,
            requisicao.Vencimento
        );

        return Responder((await despesaService.Atualizar(id, dados, ct)).Map(despesa => despesa.Adapt<DespesaDTO>()));
    }

    /// <summary>Registra a saída do dinheiro, com o comprovante.</summary>
    /// <remarks>Multipart. Sem comprovante, 400 — é ele que sustenta a prestação de contas.</remarks>
    /// <param name="id">Despesa.</param>
    /// <param name="pagoEm">Dia em que o dinheiro saiu, <c>aaaa-mm-dd</c>.</param>
    /// <param name="comprovante">PDF ou imagem; obrigatório.</param>
    [HttpPost("{id:guid}/pagar")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [Consumes("multipart/form-data")]
    [RegistrarEvento("financeiro.despesa_paga", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(DespesaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Pagar(Guid id, [FromForm] DateOnly pagoEm, IFormFile? comprovante, CancellationToken ct)
    {
        await using var conteudo = comprovante?.OpenReadStream() ?? Stream.Null;

        var resultado = await despesaService.Pagar(id, new PagarDespesa(pagoEm), usuarioAtual.Id, comprovante.ParaNovoArquivo(conteudo), ct);

        return Responder(resultado.Map(despesa => despesa.Adapt<DespesaDTO>()));
    }

    /// <summary>Cancela uma despesa prevista. A paga se corrige, não se cancela.</summary>
    /// <param name="id">Despesa.</param>
    [HttpPost("{id:guid}/cancelar")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(DespesaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct) =>
        Responder((await despesaService.Cancelar(id, usuarioAtual.Id, ct)).Map(despesa => despesa.Adapt<DespesaDTO>()));

    /// <summary>O comprovante de uma despesa.</summary>
    /// <remarks>O comprovante vai <c>inline</c>, para abrir numa aba.</remarks>
    /// <param name="id">Despesa.</param>
    [HttpGet("{id:guid}/comprovante")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BaixarComprovante(Guid id, CancellationToken ct)
    {
        var resultado = await despesaService.BaixarComprovante(id, ct);

        return Arquivo(resultado, inline: true);
    }
}
