using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Relatorios;
using Backend.Business.Abstractions;
using Backend.Business.Relatorios.Interfaces;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Relatorios;

/// <summary>
/// O painel da turma: quanto ela tem, no que gastou e quanto dela está em dia.
/// </summary>
/// <remarks>
/// <b>Nenhum número daqui aponta para uma pessoa</b> — são todos somas. A rota de gestão, que
/// devolvia a lista de inadimplentes, foi removida em 16/09/2026 junto com a tela que a consumia:
/// quem cobra trabalha na tela de Parcelas, que tem filtro, busca e paginação.
/// <para>
/// Se um dia voltar a fazer falta, ela volta como rota própria com política própria (decisão 1 da
/// Sprint 12) — e não como campo opcional aqui, que é como nome de devedor acaba vazando para a aba
/// de rede de quem não podia vê-lo.
/// </para>
/// </remarks>
/// <param name="dashboardService">Os indicadores.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboard")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class DashboardController(IDashboardService dashboardService) : MainController
{
    /// <summary>
    /// O painel do formando: quanto a turma tem, no que gastou e quanto dela está em dia.
    /// </summary>
    /// <remarks>
    /// É a tela que a comissão manda no grupo da turma quando alguém pergunta "cadê o dinheiro". Tudo
    /// aqui é soma — nenhum número aponta para uma pessoa.
    /// </remarks>
    [HttpGet("publico")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(DashboardPublicoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Publico(CancellationToken ct) =>
        Responder((await dashboardService.Publico(FormaturaId, ct)).Map(indicadores => indicadores.Adapt<DashboardPublicoDTO>()));
}
