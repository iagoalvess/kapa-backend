using Backend.Business.Abstractions;
using Backend.Business.Auth.Models;

namespace Backend.Business.Auth.Interfaces;

/// <summary>
/// Registro, login, renovação e revogação de sessão.
/// </summary>
public interface IAuthService
{
    /// <summary>Cria uma conta e já devolve a sessão.</summary>
    /// <param name="dados">Nome, e-mail e senha.</param>
    /// <param name="ipDeOrigem">IP do solicitante, registrado para auditoria.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ParDeTokens>> Registrar(RegistrarUsuario dados, string? ipDeOrigem, CancellationToken ct = default);

    /// <summary>Autentica por e-mail e senha.</summary>
    /// <param name="credenciais">E-mail e senha.</param>
    /// <param name="ipDeOrigem">IP do solicitante, registrado para auditoria.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ParDeTokens>> Autenticar(Credenciais credenciais, string? ipDeOrigem, CancellationToken ct = default);

    /// <summary>
    /// Troca um refresh token válido por um par novo, rotacionando o antigo.
    /// </summary>
    /// <param name="refreshToken">Token apresentado pelo cliente.</param>
    /// <param name="ipDeOrigem">IP do solicitante, registrado para auditoria.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ParDeTokens>> Renovar(string refreshToken, string? ipDeOrigem, CancellationToken ct = default);

    /// <summary>Revoga um refresh token — o logout desta sessão.</summary>
    /// <param name="refreshToken">Token a revogar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> Revogar(string refreshToken, CancellationToken ct = default);
}
