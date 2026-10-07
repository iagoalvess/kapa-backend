using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Assinaturas;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Services;
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
/// <param name="usuarioAtual">Quem contrata — o e-mail vai para o provedor, que o exige na recorrência.</param>
/// <param name="cupomService">Consulta do cupom antes do checkout.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/formaturas/atual/assinatura")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class AssinaturaController(IAssinaturaService assinaturaService, IUsuarioAtual usuarioAtual, CupomService cupomService) : MainController
{
    /// <summary>Status, plano, vigência e próxima cobrança. É o que a tela de retorno consulta enquanto espera.</summary>
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
    [HttpPost("checkout")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [EnableRateLimiting(RateLimitConfig.Cupom)]
    [RegistrarEvento("assinatura.checkout_iniciado")]
    [ProducesResponseType(typeof(CheckoutDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> IniciarCheckout([FromBody] IniciarCheckoutRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await assinaturaService.IniciarCheckout(
            FormaturaId,
            new IniciarCheckout(requisicao.PlanoCodigo, requisicao.Meio, usuarioAtual.Email, requisicao.CupomCodigo),
            ct
        );

        return Responder(resultado.Map(sessao => sessao.Adapt<CheckoutDTO>()));
    }

    /// <summary>
    /// Confere um cupom antes do checkout e devolve o desconto, para a tela mostrar o preço riscado (Sprint 51).
    /// </summary>
    /// <remarks>
    /// Inexistente, vencido, esgotado, desativado e turma que já pagou respondem o mesmo 400 <c>cupom.invalido</c>:
    /// distinguir viraria verificador de códigos. O limite estreito por usuário segura a força bruta.
    /// </remarks>
    /// <param name="codigo">O que foi digitado.</param>
    [HttpGet("cupom/{codigo}")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [EnableRateLimiting(RateLimitConfig.Cupom)]
    [ProducesResponseType(typeof(CupomAplicavelDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConsultarCupom(string codigo, CancellationToken ct)
    {
        var resultado = await cupomService.Consultar(FormaturaId, codigo, ct);

        return Responder(resultado.Map(cupom => cupom.Adapt<CupomAplicavelDTO>()));
    }

    /// <summary>Cancela a renovação. A vigência paga é respeitada; depois dela, a turma vira leitura.</summary>
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

    /// <summary>
    /// Troca o plano da assinatura ativa: a subida devolve a página da diferença proporcional, e o plano novo vale
    /// quando ela for paga; a descida vale na próxima renovação e devolve <c>url</c> nula.
    /// </summary>
    /// <param name="requisicao">Plano novo, do mesmo ciclo.</param>
    [HttpPost("trocar-plano")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("assinatura.plano_trocado")]
    [ProducesResponseType(typeof(TrocaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> TrocarPlano([FromBody] TrocarPlanoRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await assinaturaService.TrocarPlano(FormaturaId, requisicao.PlanoCodigo, usuarioAtual.Email, ct);

        return Responder(resultado.Map(troca => troca.Adapt<TrocaDTO>()));
    }

    /// <summary>
    /// Troca o meio de pagamento: a recorrência antiga é cancelada e a nova começa no próximo vencimento. Para o cartão,
    /// devolve a página de autorização; para o PIX, <c>url</c> nula.
    /// </summary>
    /// <param name="requisicao">Meio novo.</param>
    [HttpPost("trocar-meio")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("assinatura.meio_trocado")]
    [ProducesResponseType(typeof(TrocaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> TrocarMeio([FromBody] TrocarMeioRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await assinaturaService.TrocarMeio(requisicao.Meio, usuarioAtual.Email, ct);

        return Responder(resultado.Map(troca => troca.Adapt<TrocaDTO>()));
    }

    /// <summary>A página do PIX da renovação — só no PIX avulso, a partir de 7 dias antes do vencimento.</summary>
    [HttpPost("pagar-ciclo")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(CheckoutDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PagarCiclo(CancellationToken ct)
    {
        var resultado = await assinaturaService.PagarCiclo(usuarioAtual.Email, ct);

        return Responder(resultado.Map(sessao => sessao.Adapt<CheckoutDTO>()));
    }

    /// <summary>O histórico de pagamentos do plano: PIX de cada ciclo, débitos do cartão e diferenças de plano.</summary>
    [HttpGet("cobrancas")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(IReadOnlyList<CobrancaDoPlanoDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarCobrancas(CancellationToken ct)
    {
        var resultado = await assinaturaService.ListarCobrancas(ct);

        return Responder(resultado.Map(cobrancas => cobrancas.Adapt<IReadOnlyList<CobrancaDoPlanoDTO>>()));
    }
}
