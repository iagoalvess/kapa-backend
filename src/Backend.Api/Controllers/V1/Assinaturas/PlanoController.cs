using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Assinaturas;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Assinaturas;

/// <summary>Catálogo público de planos da licença.</summary>
/// <param name="assinaturaService">Catálogo de planos.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/planos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class PlanoController(IAssinaturaService assinaturaService) : MainController
{
    /// <summary>Planos ativos, com preço e limites. Anônimo: é vitrine.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<PlanoDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var resultado = await assinaturaService.ListarPlanos(ct);

        return Responder(resultado.Map(planos => planos.Adapt<List<PlanoDTO>>()));
    }
}
