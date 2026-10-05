using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Adesoes;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Business.Assinaturas.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Adesoes;

/// <summary>
/// A cesta do formando depois da adesão e o aditivo que a faz crescer (Sprint 48, D7/D38).
/// </summary>
/// <remarks>
/// O aditivo só acrescenta — subir de faixa ou incluir pacote — com o rito da adesão: prévia com hash, código por
/// e-mail, aceite. Descer ou tirar é a solicitação de cancelamento, em <c>/cobrancas/solicitacoes-de-cancelamento</c>.
/// </remarks>
/// <param name="aditivos">A cesta e o aditivo.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Termo)]
[Route("api/v{version:apiVersion}/adesoes")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class AditivoController(IAditivoService aditivos, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>A própria cesta e o que o aditivo pode acrescentar.</summary>
    [HttpGet("minha-cesta")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(MinhaCestaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ObterCesta(CancellationToken ct) =>
        Responder((await aditivos.ObterCesta(FormaturaId, usuarioAtual.Id, ct)).Map(cesta => cesta.Adapt<MinhaCestaDTO>()));

    /// <summary>O aditivo antes do aceite: o que muda, as parcelas novas e o hash a devolver.</summary>
    /// <remarks>
    /// Descer de faixa, 409 <c>adesao.aditivo_so_acrescenta</c>; pacote já na cesta, 400 <c>adesao.pacote_ja_contratado</c>.
    /// </remarks>
    /// <param name="requisicao">Os pacotes que entram.</param>
    [HttpPost("aditivo/previa")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(PreviaDoAditivoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Simular([FromBody] SimularAditivoRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (await aditivos.Simular(FormaturaId, usuarioAtual.Id, requisicao.Pacotes ?? [], ct)).Map(previa => previa.Adapt<PreviaDoAditivoDTO>())
        );

    /// <summary>Manda o código que confirma o aceite do aditivo para o e-mail da conta.</summary>
    [HttpPost("aditivo/codigo")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [EnableRateLimiting(RateLimitConfig.Codigo)]
    [ProducesResponseType(typeof(CodigoEnviadoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SolicitarCodigo(CancellationToken ct) =>
        Responder((await aditivos.SolicitarCodigo(FormaturaId, usuarioAtual.Id, ct)).Map(envio => envio.Adapt<CodigoEnviadoDTO>()));

    /// <summary>Aceita o aditivo: a cesta muda e as parcelas da diferença nascem, na mesma transação.</summary>
    /// <remarks>409 com <c>adesao.aditivo_desatualizado</c> (peça a prévia de novo) ou <c>adesao.codigo_invalido</c>.</remarks>
    /// <param name="requisicao">Pacotes, hash da prévia, código e o detalhe livre.</param>
    [HttpPost("aditivo")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [EnableRateLimiting(RateLimitConfig.Codigo)]
    [ProducesResponseType(typeof(PreviaDoAditivoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Aceitar([FromBody] AceitarAditivoRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (
                await aditivos.Aceitar(
                    FormaturaId,
                    usuarioAtual.Id,
                    new AceitarAditivo(
                        requisicao.Pacotes ?? [],
                        requisicao.HashDoConteudo ?? string.Empty,
                        requisicao.Codigo ?? string.Empty,
                        requisicao.Observacoes?.Select(observacao => new ObservacaoDoPacote(observacao.PacoteId, observacao.Texto)).ToList()
                    ),
                    usuarioAtual.Origem,
                    ct
                )
            ).Map(previa => previa.Adapt<PreviaDoAditivoDTO>())
        );
}
