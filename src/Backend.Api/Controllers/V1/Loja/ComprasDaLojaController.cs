using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Loja;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Loja.Interfaces;
using Backend.Business.Loja.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Loja;

/// <summary>
/// As compras da loja, do lado da Gestão: a lista que sustenta a devolução (P5) e a conta do que está preso
/// esperando pagamento (Sprint 26, decisão 3).
/// </summary>
/// <remarks>Leitura, então sem política de status: a turma encerrada ainda precisa da lista para devolver.</remarks>
/// <param name="loja">Regras da loja.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Cobrancas)]
[Route("api/v{version:apiVersion}/loja/compras")]
[Authorize(Policy = Politicas.Gestao)]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ComprasDaLojaController(ILojaService loja) : MainController
{
    /// <summary>As compras da turma, da mais nova para a mais antiga.</summary>
    /// <param name="paginacao">Página e tamanho.</param>
    /// <param name="status"><c>Pendente</c>, <c>Paga</c>, <c>Expirada</c> ou <c>ADevolver</c>.</param>
    /// <param name="busca">Trecho do nome ou do e-mail.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PaginaDTO<CompraNaGestaoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] StatusDaCompra? status,
        [FromQuery] string? busca,
        CancellationToken ct
    ) =>
        Responder(
            (await loja.Listar(paginacao.ParaModelo(), new FiltroDeCompras(status, busca), ct)).Map(pagina =>
                pagina.ParaDTO(compra => compra.Adapt<CompraNaGestaoDTO>())
            )
        );

    /// <summary>O que vendeu, o que está preso esperando PIX, e o que falta devolver.</summary>
    [HttpGet("resumo")]
    [ProducesResponseType(typeof(ResumoDaLojaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resumir(CancellationToken ct) =>
        Responder((await loja.Resumir(ct)).Map(resumo => resumo.Adapt<ResumoDaLojaDTO>()));

    /// <summary>A lista em planilha, com nome, e-mail, valores e situação — para a comissão devolver (P5).</summary>
    /// <param name="status">Só um status.</param>
    /// <param name="busca">Trecho do nome ou do e-mail.</param>
    [HttpGet("planilha")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Exportar([FromQuery] StatusDaCompra? status, [FromQuery] string? busca, CancellationToken ct) =>
        Arquivo(await loja.Exportar(new FiltroDeCompras(status, busca), ct));
}
