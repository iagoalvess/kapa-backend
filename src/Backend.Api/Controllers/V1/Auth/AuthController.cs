using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Auth;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Legal.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Auth;

/// <summary>
/// Registro, login, renovação e encerramento de sessão.
/// </summary>
/// <remarks>
/// Todos os endpoints são anônimos e limitados por IP — endpoint de login sem limite é um oráculo de
/// força bruta contra as senhas dos usuários. Cadastro e login usam o balde de
/// <see cref="RateLimitConfig.Entrada"/>, que aceita a assembleia inteira no mesmo Wi-Fi; renovação e
/// saída, o de <see cref="RateLimitConfig.Sessao"/>.
/// <para>
/// <b>O refresh token trafega em cookie <c>HttpOnly</c> por padrão</b> e, nesse modo, não aparece
/// no corpo de nenhuma resposta. Ver <c>docs/decisoes.md</c>, item 17.
/// </para>
/// </remarks>
/// <param name="authService">Regras de autenticação.</param>
/// <param name="usuarioAtual">IP e navegador da requisição, gravados no consentimento do cadastro.</param>
/// <param name="sessao">De onde vem o refresh token e como o par volta ao cliente.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitConfig.Autenticacao)]
public sealed class AuthController(IAuthService authService, IUsuarioAtual usuarioAtual, SessaoHttp sessao) : MainController
{
    private string? IpDeOrigem => usuarioAtual.EnderecoIp;

    /// <summary>Cria uma conta, registra o aceite dos documentos legais e já devolve a sessão.</summary>
    /// <remarks>
    /// Sem o aceite de cada documento vigente, responde 400 <c>legal.aceite_obrigatorio</c> e a
    /// conta não é criada. Versão que deixou de ser a vigente responde 409
    /// <c>legal.versao_desatualizada</c>.
    /// </remarks>
    /// <param name="requisicao">Nome, e-mail, senha e as versões aceitas.</param>
    [HttpPost("registrar")]
    [EnableRateLimiting(RateLimitConfig.Entrada)]
    [ProducesResponseType(typeof(TokenResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Registrar([FromBody] RegistrarRequestDTO requisicao, CancellationToken ct)
    {
        var dados = new RegistrarUsuario(
            requisicao.Nome,
            requisicao.Email,
            requisicao.Senha,
            [.. (requisicao.Aceites ?? []).OfType<AceiteDeDocumentoDTO>().Select(aceite => new AceiteDeDocumento(aceite.Tipo, aceite.Versao))],
            requisicao.ReceberComunicacaoDoKapa ?? false
        );

        var resultado = await authService.Registrar(dados, usuarioAtual.Origem, ct);

        return ResponderComSessao(resultado);
    }

    /// <summary>Autentica por e-mail e senha.</summary>
    /// <remarks>
    /// Administrador e presidente não recebem a sessão aqui: 202 com o desafio, e o código de seis dígitos vai ao
    /// e-mail da conta (login em duas etapas, revisão de segurança de 05/10/2026). A sessão sai em
    /// <c>POST login/codigo</c>.
    /// </remarks>
    /// <param name="requisicao">Credenciais.</param>
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitConfig.Entrada)]
    [ProducesResponseType(typeof(TokenResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CodigoDeEntradaDTO), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await authService.Autenticar(new Credenciais(requisicao.Email, requisicao.Senha), IpDeOrigem, ct);

        if (resultado is { Sucesso: true, Valor.Codigo: { } codigo })
            return Accepted(new CodigoDeEntradaDTO(codigo.Desafio, codigo.EnviadoPara, codigo.MinutosDeValidade));

        return ResponderComSessao(resultado.Map(entrada => entrada.Sessao!));
    }

    /// <summary>O segundo passo do login: troca o desafio e o código do e-mail pela sessão.</summary>
    /// <remarks>
    /// 401 <c>auth.codigo_incorreto</c> com código errado ou vencido; <c>auth.codigo_bloqueado</c> depois de cinco
    /// erros na conta, por quinze minutos; <c>auth.desafio_invalido</c> com o desafio vencido ou adulterado.
    /// </remarks>
    /// <param name="requisicao">O desafio e o código.</param>
    [HttpPost("login/codigo")]
    [EnableRateLimiting(RateLimitConfig.Entrada)]
    [ProducesResponseType(typeof(TokenResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ConfirmarCodigo([FromBody] CodigoDeEntradaRequestDTO requisicao, CancellationToken ct) =>
        ResponderComSessao(await authService.ConfirmarCodigo(requisicao.Desafio, requisicao.Codigo, IpDeOrigem, ct));

    /// <summary>Manda o código do login de novo.</summary>
    /// <param name="requisicao">O desafio do login.</param>
    [HttpPost("login/codigo/reenviar")]
    [EnableRateLimiting(RateLimitConfig.Entrada)]
    [ProducesResponseType(typeof(CodigoDeEntradaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReenviarCodigo([FromBody] ReenvioDoCodigoRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (await authService.ReenviarCodigo(requisicao.Desafio, ct)).Map(codigo => new CodigoDeEntradaDTO(
                codigo.Desafio,
                codigo.EnviadoPara,
                codigo.MinutosDeValidade
            ))
        );

    /// <summary>
    /// Troca um refresh token válido por um par novo.
    /// </summary>
    /// <remarks>
    /// No modo cookie o corpo é dispensável — o token vem do cookie <c>HttpOnly</c>. Sessão
    /// inválida apaga o cookie junto com o 401: deixá-lo para trás faria o navegador repetir a
    /// mesma renovação fadada a falhar a cada carga da página.
    /// </remarks>
    /// <param name="requisicao">Refresh token, quando o modo cookie está desligado.</param>
    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitConfig.Sessao)]
    [ProducesResponseType(typeof(TokenResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Renovar([FromBody] RefreshRequestDTO? requisicao, CancellationToken ct)
    {
        var resultado = await authService.Renovar(TokenRecebido(requisicao), IpDeOrigem, ct);

        if (resultado.Falhou)
            sessao.ApagarCookie();

        return ResponderComSessao(resultado);
    }

    /// <summary>Encerra a sessão associada ao refresh token informado.</summary>
    /// <param name="requisicao">Refresh token, quando o modo cookie está desligado.</param>
    [HttpPost("logout")]
    [EnableRateLimiting(RateLimitConfig.Sessao)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequestDTO? requisicao, CancellationToken ct)
    {
        var resultado = await authService.Revogar(TokenRecebido(requisicao), ct);

        sessao.ApagarCookie();

        return Responder(resultado);
    }

    private string TokenRecebido(RefreshRequestDTO? requisicao) => sessao.RefreshTokenRecebido(requisicao?.RefreshToken);

    /// <summary>
    /// Devolve o par de tokens, mandando o refresh pelo cookie quando o modo está ligado.
    /// </summary>
    /// <param name="resultado">Resultado devolvido pelo service.</param>
    private IActionResult ResponderComSessao(Result<ParDeTokens> resultado) => Responder(sessao.Preparar(resultado));
}
