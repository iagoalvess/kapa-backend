using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Auth;
using Backend.Api.DTOs.Formaturas;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Settings;
using Backend.Business.Formaturas.Interfaces;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Backend.Api.Controllers.V1.Formaturas;

/// <summary>
/// Quais formaturas o usuário acessa e qual delas a sessão está enxergando.
/// </summary>
/// <remarks>
/// Os dois endpoints exigem apenas autenticação, e não formatura selecionada: são o caminho
/// para selecionar uma. Exigir a claim aqui deixaria o usuário preso num 403 sem saída.
/// </remarks>
/// <param name="formaturaService">Listagem e seleção de formatura.</param>
/// <param name="usuarioAtual">Quem está fazendo a requisição.</param>
/// <param name="cookieOptions">Configuração do cookie de sessão.</param>
/// <param name="jwtOptions">Configuração de JWT, que define a validade do cookie.</param>
/// <param name="configuration">Configuração da aplicação, de onde saem as origens permitidas.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/formaturas")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class FormaturaController(
    IFormaturaService formaturaService,
    IUsuarioAtual usuarioAtual,
    IOptions<CookieDeSessaoSettings> cookieOptions,
    IOptions<JwtSettings> jwtOptions,
    IConfiguration configuration
) : MainController
{
    private string? IpDeOrigem => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string[] OrigensPermitidas => configuration.GetSection(ApiConfig.SecaoDeOrigens).Get<string[]>() ?? [];

    /// <summary>Lista as formaturas em que o usuário tem vínculo ativo.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("minhas")]
    [Authorize(Policy = Politicas.Autenticado)]
    [ProducesResponseType(typeof(IReadOnlyList<FormaturaDoUsuarioDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarMinhas(CancellationToken ct)
    {
        var resultado = await formaturaService.ListarMinhas(usuarioAtual.Id, ct);

        return Responder(resultado.Map(formaturas => formaturas.Adapt<List<FormaturaDoUsuarioDTO>>()));
    }

    /// <summary>
    /// Passa a sessão a enxergar a formatura informada.
    /// </summary>
    /// <remarks>
    /// Devolve o mesmo <c>ParDeTokens</c> do login — o front reaproveita o caminho de sessão
    /// que já existe, em vez de ganhar um segundo jeito de guardar credencial. O refresh token
    /// atual é rotacionado junto, para a troca não deixar uma sessão viva para trás.
    /// </remarks>
    /// <param name="id">Formatura pretendida.</param>
    /// <param name="requisicao">Refresh token atual, quando o modo cookie está desligado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("{id:guid}/selecionar")]
    [Authorize(Policy = Politicas.Autenticado)]
    [RegistrarEvento("formatura.selecionada", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(TokenResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Selecionar(Guid id, [FromBody] RefreshRequestDTO? requisicao, CancellationToken ct)
    {
        var refreshAtual = Request.RefreshTokenRecebido(cookieOptions.Value, OrigensPermitidas, requisicao?.RefreshToken);
        var resultado = await formaturaService.Selecionar(usuarioAtual.Id, id, refreshAtual, IpDeOrigem, ct);

        return Responder(RespostaDeSessao.Preparar(resultado, Response, cookieOptions.Value, jwtOptions.Value));
    }
}
