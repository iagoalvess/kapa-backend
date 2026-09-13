using System.Security.Claims;
using Backend.Business.Auth.Services;
using Backend.Business.Usuarios.Models;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Backend.Api.Extensions;

/// <summary>
/// Quem está fazendo a requisição.
/// </summary>
/// <remarks>
/// Existe para que services e controllers não precisem escarafunchar <c>ClaimsPrincipal</c>.
/// Fica na Api, e não em Business: identidade vem do transporte HTTP, e a camada de negócio
/// recebe o <c>Guid</c> por parâmetro em vez de depender de contexto ambiente.
/// </remarks>
public interface IUsuarioAtual
{
    /// <summary>Indica se a requisição está autenticada.</summary>
    bool Autenticado { get; }

    /// <summary>Identificador do usuário. <see cref="Guid.Empty"/> quando anônimo.</summary>
    Guid Id { get; }

    /// <summary>E-mail do usuário, quando autenticado.</summary>
    string? Email { get; }

    /// <summary>Perfis presentes no token.</summary>
    IReadOnlyList<string> Perfis { get; }

    /// <summary>Indica se o usuário é administrador.</summary>
    bool EhAdministrador { get; }

    /// <summary>
    /// IP do cliente.
    /// </summary>
    /// <remarks>
    /// Atrás de proxy, só é o do cliente com <c>Rede:ProxiesConfiaveis</c> configurado — sem isso é
    /// o do load balancer (ver <c>RedeConfig</c>).
    /// </remarks>
    string? EnderecoIp { get; }

    /// <summary>Cabeçalho <c>User-Agent</c> da requisição.</summary>
    string? UserAgent { get; }
}

/// <summary>
/// Implementação de <see cref="IUsuarioAtual"/> sobre o contexto HTTP.
/// </summary>
/// <param name="accessor">Acesso ao contexto da requisição.</param>
public sealed class UsuarioAtual(IHttpContextAccessor accessor) : IUsuarioAtual
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    /// <inheritdoc />
    public bool Autenticado => Principal?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public Guid Id => Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : Guid.Empty;

    /// <inheritdoc />
    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email);

    /// <inheritdoc />
    public IReadOnlyList<string> Perfis => Principal?.FindAll(TokenService.ClaimDePerfil).Select(claim => claim.Value).ToArray() ?? [];

    /// <inheritdoc />
    public bool EhAdministrador => Perfis.Contains(PerfisPadrao.Administrador, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string? EnderecoIp => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    /// <inheritdoc />
    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString();
}
