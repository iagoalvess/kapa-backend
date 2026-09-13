using Backend.Business.Abstractions;
using Backend.Business.Auth.Models;
using Backend.Business.Legal.Models;

namespace Backend.Business.Auth.Interfaces;

/// <summary>
/// Registro, login, renovação e revogação de sessão.
/// </summary>
public interface IAuthService
{
    /// <summary>Cria uma conta com o consentimento aos documentos legais e já devolve a sessão.</summary>
    /// <param name="dados">Nome, e-mail, senha e as versões aceitas.</param>
    /// <param name="origem">IP e navegador do solicitante, gravados no consentimento e na sessão.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ParDeTokens>> Registrar(RegistrarUsuario dados, OrigemDoAceite origem, CancellationToken ct = default);

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

    /// <summary>
    /// Emite uma sessão já apontando para uma formatura.
    /// </summary>
    /// <remarks>
    /// Quem confere o vínculo é o <c>IFormaturaService</c>; aqui a formatura já chega
    /// verificada. A emissão mora neste service para não existirem dois lugares gravando
    /// refresh token — e divergindo no dia em que a rotação mudar.
    /// </remarks>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="formaturaId">Formatura cujo vínculo já foi verificado.</param>
    /// <param name="papel">Papel do usuário nessa formatura.</param>
    /// <param name="refreshTokenAtual">Refresh token da sessão atual, obrigatório e revogado em favor do novo.</param>
    /// <param name="ipDeOrigem">IP do solicitante, registrado para auditoria.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ParDeTokens>> EmitirSessaoDeFormatura(
        Guid usuarioId,
        Guid formaturaId,
        string papel,
        string refreshTokenAtual,
        string? ipDeOrigem,
        CancellationToken ct = default
    );

    /// <summary>Revoga um refresh token — o logout desta sessão.</summary>
    /// <param name="refreshToken">Token a revogar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> Revogar(string refreshToken, CancellationToken ct = default);
}
