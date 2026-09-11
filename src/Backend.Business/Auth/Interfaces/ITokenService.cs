using Backend.Business.Auth.Models;
using Backend.Business.Usuarios.Models;

namespace Backend.Business.Auth.Interfaces;

/// <summary>
/// Emissão de tokens. Não conhece banco nem regra de login — só produz credenciais.
/// </summary>
public interface ITokenService
{
    /// <summary>Emite um access token assinado para o usuário.</summary>
    /// <param name="usuario">Usuário autenticado.</param>
    /// <param name="perfis">Perfis do usuário, que viram claims de papel.</param>
    AccessTokenGerado GerarAccessToken(Usuario usuario, IReadOnlyList<string> perfis);

    /// <summary>Gera um refresh token aleatório, devolvendo o valor e o hash a persistir.</summary>
    RefreshTokenGerado GerarRefreshToken();

    /// <summary>Calcula o hash de um refresh token apresentado pelo cliente, para busca no banco.</summary>
    /// <param name="tokenEmTextoPuro">Token como o cliente o enviou.</param>
    string CalcularHash(string tokenEmTextoPuro);
}
