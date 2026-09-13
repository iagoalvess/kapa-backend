using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Auth;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Auth.Settings;
using Backend.Business.Legal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Backend.Api.Controllers.V1.Auth;

/// <summary>
/// Registro, login, renovação e encerramento de sessão.
/// </summary>
/// <remarks>
/// Todos os endpoints são anônimos e ficam sob o limite estreito de
/// <see cref="RateLimitConfig.Autenticacao"/> — endpoint de login sem limite é um oráculo de
/// força bruta contra as senhas dos usuários.
/// <para>
/// <b>O refresh token trafega em cookie <c>HttpOnly</c> por padrão</b> e, nesse modo, não aparece
/// no corpo de nenhuma resposta. Ver <c>docs/decisoes.md</c>, item 17.
/// </para>
/// </remarks>
/// <param name="authService">Regras de autenticação.</param>
/// <param name="usuarioAtual">IP e navegador da requisição, gravados no consentimento do cadastro.</param>
/// <param name="cookieOptions">Configuração do cookie de sessão.</param>
/// <param name="jwtOptions">Configuração de JWT, que define a validade do cookie.</param>
/// <param name="configuration">Configuração da aplicação, de onde saem as origens permitidas.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitConfig.Autenticacao)]
public sealed class AuthController(
    IAuthService authService,
    IUsuarioAtual usuarioAtual,
    IOptions<CookieDeSessaoSettings> cookieOptions,
    IOptions<JwtSettings> jwtOptions,
    IConfiguration configuration
) : MainController
{
    private string? IpDeOrigem => usuarioAtual.EnderecoIp;

    private CookieDeSessaoSettings Cookie => cookieOptions.Value;

    private string[] OrigensPermitidas => configuration.GetSection(ApiConfig.SecaoDeOrigens).Get<string[]>() ?? [];

    /// <summary>Cria uma conta, registra o aceite dos documentos legais e já devolve a sessão.</summary>
    /// <remarks>
    /// Sem o aceite de cada documento vigente, responde 400 <c>legal.aceite_obrigatorio</c> e a
    /// conta não é criada. Versão que deixou de ser a vigente responde 409
    /// <c>legal.versao_desatualizada</c>.
    /// </remarks>
    /// <param name="requisicao">Nome, e-mail, senha e as versões aceitas.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("registrar")]
    [ProducesResponseType(typeof(TokenResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Registrar([FromBody] RegistrarRequestDTO requisicao, CancellationToken ct)
    {
        var dados = new RegistrarUsuario(
            requisicao.Nome,
            requisicao.Email,
            requisicao.Senha,
            [.. (requisicao.Aceites ?? []).OfType<AceiteDeDocumentoDTO>().Select(aceite => new AceiteDeDocumento(aceite.Tipo, aceite.Versao))]
        );

        var resultado = await authService.Registrar(dados, new OrigemDoAceite(usuarioAtual.EnderecoIp, usuarioAtual.UserAgent), ct);

        return ResponderComSessao(resultado);
    }

    /// <summary>Autentica por e-mail e senha.</summary>
    /// <param name="requisicao">Credenciais.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("login")]
    [ProducesResponseType(typeof(TokenResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await authService.Autenticar(new Credenciais(requisicao.Email, requisicao.Senha), IpDeOrigem, ct);

        return ResponderComSessao(resultado);
    }

    /// <summary>
    /// Troca um refresh token válido por um par novo.
    /// </summary>
    /// <remarks>
    /// No modo cookie o corpo é dispensável — o token vem do cookie <c>HttpOnly</c>. Sessão
    /// inválida apaga o cookie junto com o 401: deixá-lo para trás faria o navegador repetir a
    /// mesma renovação fadada a falhar a cada carga da página.
    /// </remarks>
    /// <param name="requisicao">Refresh token, quando o modo cookie está desligado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(TokenResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Renovar([FromBody] RefreshRequestDTO? requisicao, CancellationToken ct)
    {
        var resultado = await authService.Renovar(TokenRecebido(requisicao), IpDeOrigem, ct);

        if (resultado.Falhou && Cookie.Habilitado)
            Response.Apagar(Cookie);

        return ResponderComSessao(resultado);
    }

    /// <summary>Encerra a sessão associada ao refresh token informado.</summary>
    /// <param name="requisicao">Refresh token, quando o modo cookie está desligado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequestDTO? requisicao, CancellationToken ct)
    {
        var resultado = await authService.Revogar(TokenRecebido(requisicao), ct);

        if (Cookie.Habilitado)
            Response.Apagar(Cookie);

        return Responder(resultado);
    }

    private string TokenRecebido(RefreshRequestDTO? requisicao) => Request.RefreshTokenRecebido(Cookie, OrigensPermitidas, requisicao?.RefreshToken);

    /// <summary>
    /// Devolve o par de tokens, mandando o refresh pelo cookie quando o modo está ligado.
    /// </summary>
    /// <param name="resultado">Resultado devolvido pelo service.</param>
    private IActionResult ResponderComSessao(Result<ParDeTokens> resultado) =>
        Responder(RespostaDeSessao.Preparar(resultado, Response, Cookie, jwtOptions.Value));
}
