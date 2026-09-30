using System.Text;
using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Business.MercadoPago.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.MercadoPago;

/// <summary>
/// O aviso de pagamento do Mercado Pago (webhook) — o recebedor único, das turmas e da conta do Kapa
/// (Sprint 25; Sprint 35).
/// </summary>
/// <remarks>
/// Anônimo por natureza, e não confia no que chega: o aviso só é aceito com a assinatura conferida e o valor
/// pago é perguntado ao Mercado Pago (decisão 12).
/// </remarks>
/// <param name="aviso">O processamento do aviso.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[EnableRateLimiting(RateLimitConfig.Webhook)]
public sealed class AvisoDoMercadoPagoController(IAvisoDoMercadoPago aviso) : MainController
{
    /// <summary>O aviso do Mercado Pago — o recebedor único, das turmas e da conta do Kapa (Sprint 35).</summary>
    /// <remarks>
    /// 200 quando o aviso foi entendido — inclusive o de pedido que não é do Kapa, senão ele seria
    /// reentregue para sempre. 401 com assinatura inválida; 503 quando a consulta ao Mercado Pago falhou, e
    /// ele reentrega. Do corpo só se lê o <c>user_id</c>, que decide se o aviso é da conta do Kapa.
    /// </remarks>
    /// <param name="idDoRecurso">O id do recurso (<c>data.id</c>).</param>
    /// <param name="tipo">O tópico (<c>type</c>).</param>
    [HttpPost("webhooks/cobranca/mercadopago")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Aviso(
        [FromQuery(Name = "data.id")] string? idDoRecurso,
        [FromQuery(Name = "type")] string? tipo,
        CancellationToken ct
    )
    {
        using var leitor = new StreamReader(Request.Body, Encoding.UTF8);
        var corpo = await leitor.ReadToEndAsync(ct);

        var resultado = await aviso.Receber(
            Request.Headers["x-signature"].ToString(),
            Request.Headers["x-request-id"].ToString(),
            idDoRecurso,
            tipo,
            corpo,
            ct
        );

        return resultado.Sucesso ? Ok() : Responder(resultado);
    }
}
