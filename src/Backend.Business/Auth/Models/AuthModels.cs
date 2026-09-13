using Backend.Business.Legal.Models;

namespace Backend.Business.Auth.Models;

/// <summary>Credenciais apresentadas no login.</summary>
/// <param name="Email">E-mail cadastrado.</param>
/// <param name="Senha">Senha em texto puro, usada apenas para conferir o hash.</param>
public sealed record Credenciais(string Email, string Senha);

/// <summary>Dados de criação de conta.</summary>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail, que também é o login.</param>
/// <param name="Senha">Senha em texto puro.</param>
/// <param name="Aceites">Versões dos documentos legais aceitas no cadastro — uma por documento.</param>
public sealed record RegistrarUsuario(string Nome, string Email, string Senha, IReadOnlyList<AceiteDeDocumento> Aceites);

/// <summary>
/// Par de tokens devolvido ao cliente após autenticar ou renovar.
/// </summary>
/// <param name="AccessToken">JWT a enviar no cabeçalho <c>Authorization: Bearer</c>.</param>
/// <param name="ExpiraEm">Expiração do access token, em UTC.</param>
/// <param name="RefreshToken">Token opaco usado para obter o próximo par. Guarde com o mesmo cuidado de uma senha.</param>
public sealed record ParDeTokens(string AccessToken, DateTime ExpiraEm, string RefreshToken);

/// <summary>Access token recém-emitido.</summary>
/// <param name="Token">JWT assinado.</param>
/// <param name="ExpiraEm">Expiração, em UTC.</param>
public sealed record AccessTokenGerado(string Token, DateTime ExpiraEm);

/// <summary>
/// Refresh token recém-gerado, nas duas formas.
/// </summary>
/// <param name="Token">Valor entregue ao cliente. Não persista.</param>
/// <param name="Hash">Valor persistido. Não entregue ao cliente.</param>
/// <param name="ExpiraEm">Expiração, em UTC.</param>
public sealed record RefreshTokenGerado(string Token, string Hash, DateTime ExpiraEm);
