using System.Text;
using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Assinaturas;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Settings;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Backend.Api.Controllers.V1.Assinaturas;

/// <summary>
/// Eventos do provedor de assinatura — a fonte da verdade do pagamento.
/// </summary>
/// <remarks>
/// Fora de <c>/formaturas/atual</c>: não há token nem formatura selecionada. A formatura vem do
/// payload, e por isso a assinatura HMAC é conferida antes de qualquer outra coisa.
/// <para>
/// Rate limit próprio e folgado: apertado demais faz o provedor desistir de reentregar.
/// </para>
/// </remarks>
/// <param name="webhookService">Verificação e aplicação do evento.</param>
/// <param name="settings">Cabeçalho da assinatura HMAC.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webhooks")]
[EnableRateLimiting(RateLimitConfig.Webhook)]
public sealed class WebhookController(IWebhookService webhookService, IOptions<AssinaturaSettings> settings) : MainController
{
    /// <summary>
    /// Recebe um evento de assinatura. Repetido responde 200 sem reprocessar; HMAC inválido, 401 sem gravar nada.
    /// </summary>
    /// <remarks>O corpo é lido cru: reserializar mudaria os bytes e o HMAC não conferiria.</remarks>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("assinaturas")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ReciboDeWebhookDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Assinaturas(CancellationToken ct)
    {
        using var leitor = new StreamReader(Request.Body, Encoding.UTF8);
        var corpo = await leitor.ReadToEndAsync(ct);

        var resultado = await webhookService.Receber(corpo, Request.Headers[settings.Value.CabecalhoDaAssinatura].ToString(), ct);

        return Responder(resultado.Map(recibo => recibo.Adapt<ReciboDeWebhookDTO>()));
    }
}
