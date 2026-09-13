using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Assinaturas;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Assinaturas;

/// <summary>
/// A assinatura da licença, do lado da comissão: contratar, acompanhar e cancelar.
/// </summary>
/// <remarks>
/// O checkout <b>não</b> exige formatura ativa — é justamente o caminho para ativá-la, e para a
/// suspensa voltar. Cancelar exige: só se cancela a renovação do que está valendo.
/// </remarks>
/// <param name="assinaturaService">Contratação e cancelamento.</param>
/// <param name="formaturaAtual">Formatura da sessão.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/formaturas/atual/assinatura")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class AssinaturaController(IAssinaturaService assinaturaService, IFormaturaAtual formaturaAtual) : MainController
{
    /// <summary>A política garante a claim; o <c>Guid.Empty</c> nunca chega a ser consultado.</summary>
    private Guid FormaturaId => formaturaAtual.Id ?? Guid.Empty;

    /// <summary>Status, plano, vigência e próxima cobrança. É o que a tela de retorno consulta enquanto espera.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(AssinaturaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterAtual(CancellationToken ct)
    {
        var resultado = await assinaturaService.ObterAtual(ct);

        return Responder(resultado.Map(assinatura => assinatura.Adapt<AssinaturaDTO>()));
    }

    /// <summary>
    /// Cria a assinatura pendente e a sessão no provedor; devolve a URL da página de pagamento.
    /// </summary>
    /// <remarks>Não ativa nada: quem ativa é o webhook, quando o pagamento confirma.</remarks>
    /// <param name="requisicao">Plano escolhido.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("checkout")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [RegistrarEvento("assinatura.checkout_iniciado")]
    [ProducesResponseType(typeof(CheckoutDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> IniciarCheckout([FromBody] IniciarCheckoutRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await assinaturaService.IniciarCheckout(FormaturaId, new IniciarCheckout(requisicao.PlanoCodigo), ct);

        return Responder(resultado.Map(sessao => sessao.Adapt<CheckoutDTO>()));
    }

    /// <summary>Cancela a renovação. A vigência paga é respeitada; depois dela, a turma vira leitura.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("cancelar")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("assinatura.cancelada")]
    [ProducesResponseType(typeof(AssinaturaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancelar(CancellationToken ct)
    {
        var resultado = await assinaturaService.Cancelar(ct);

        return Responder(resultado.Map(assinatura => assinatura.Adapt<AssinaturaDTO>()));
    }
}
