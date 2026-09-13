using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Auth;
using Backend.Api.DTOs.Convites;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Settings;
using Backend.Business.Convites.Interfaces;
using Backend.Business.Legal.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Backend.Api.Controllers.V1.Convites;

/// <summary>
/// O convite do lado de quem foi convidado: ver a turma e entrar nela.
/// </summary>
/// <remarks>
/// Sob o limite estreito de <see cref="RateLimitConfig.Convites"/>: é o único endpoint em que
/// adivinhar um valor daria acesso a uma formatura.
/// <para>
/// Token inexistente, expirado, revogado e esgotado respondem o mesmo 404 <c>convite.invalido</c>.
/// </para>
/// </remarks>
/// <param name="conviteService">Consulta e aceite.</param>
/// <param name="usuarioAtual">Quem está aceitando, com IP e navegador.</param>
/// <param name="cookieOptions">Configuração do cookie de sessão.</param>
/// <param name="jwtOptions">Configuração de JWT, que define a validade do cookie.</param>
/// <param name="configuration">Configuração da aplicação, de onde saem as origens permitidas.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/convites")]
[EnableRateLimiting(RateLimitConfig.Convites)]
public sealed class AceiteDeConviteController(
    IConviteService conviteService,
    IUsuarioAtual usuarioAtual,
    IOptions<CookieDeSessaoSettings> cookieOptions,
    IOptions<JwtSettings> jwtOptions,
    IConfiguration configuration
) : MainController
{
    private string[] OrigensPermitidas => configuration.GetSection(ApiConfig.SecaoDeOrigens).Get<string[]>() ?? [];

    /// <summary>
    /// Turma, instituição e papel oferecido. Público.
    /// </summary>
    /// <remarks>A URL circula em grupo de WhatsApp: nunca membros, valores ou quem convidou.</remarks>
    /// <param name="token">Token do link.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("{token}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ConvitePublicoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(string token, CancellationToken ct)
    {
        var resultado = await conviteService.ObterPublico(token, ct);

        return Responder(resultado.Map(convite => convite.Adapt<ConvitePublicoDTO>()));
    }

    /// <summary>
    /// Vincula a conta autenticada à turma do convite e devolve a sessão já dentro dela.
    /// </summary>
    /// <remarks>
    /// O corpo só carrega o refresh token, e só com o modo cookie desligado. Papel enviado nele é
    /// ignorado: o papel é o do convite.
    /// </remarks>
    /// <param name="token">Token do link.</param>
    /// <param name="requisicao">Refresh token atual, quando o modo cookie está desligado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("{token}/aceitar")]
    [Authorize(Policy = Politicas.Autenticado)]
    [RegistrarEvento("convite.aceito")]
    [ProducesResponseType(typeof(TokenResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Aceitar(string token, [FromBody] RefreshRequestDTO? requisicao, CancellationToken ct)
    {
        var refreshAtual = Request.RefreshTokenRecebido(cookieOptions.Value, OrigensPermitidas, requisicao?.RefreshToken);

        var resultado = await conviteService.Aceitar(
            usuarioAtual.Id,
            token,
            refreshAtual,
            new OrigemDoAceite(usuarioAtual.EnderecoIp, usuarioAtual.UserAgent),
            ct
        );

        return Responder(RespostaDeSessao.Preparar(resultado, Response, cookieOptions.Value, jwtOptions.Value));
    }
}
