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

/// <summary>
/// O plano que vale para a turma agora e os módulos que ele libera (Sprint 45).
/// </summary>
/// <remarks>
/// Separado de <see cref="AssinaturaController"/> porque as perguntas são outras. A assinatura é da Gestão
/// e não existe no gratuito (404). O plano, todo membro lê, e sempre há um: é com ele que a tela tranca a
/// área fora do plano antes de chamar a API, em vez de mostrar o 403 como erro.
/// <para>
/// Leitura, e não autorização: quem recusa continua sendo <c>[ExigeModulo]</c> em cada área.
/// </para>
/// </remarks>
/// <param name="assinaturaService">O plano vigente.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/formaturas/atual/plano")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class PlanoDaTurmaController(IAssinaturaService assinaturaService) : MainController
{
    /// <summary>O plano vigente da turma da sessão, com os módulos.</summary>
    /// <remarks>Aceita o desligado, como a moldura: é o menu dele também.</remarks>
    [HttpGet]
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [ProducesResponseType(typeof(PlanoDaTurmaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Obter(CancellationToken ct) =>
        Responder((await assinaturaService.ObterPlanoDaTurma(FormaturaId, ct)).Map(plano => plano.Adapt<PlanoDaTurmaDTO>()));
}
