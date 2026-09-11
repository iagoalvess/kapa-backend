namespace Backend.Api.DTOs.Auth;

/// <summary>
/// Corpo do pedido de login.
/// </summary>
/// <remarks>
/// Sem atributos de validação de propósito: a regra vive uma única vez, nos validadores da
/// camada de negócio. Repetir aqui garante que um dia as duas versões discordem — e a que o
/// teste cobre não é a que roda.
/// </remarks>
/// <param name="Email">E-mail cadastrado.</param>
/// <param name="Senha">Senha.</param>
public sealed record LoginRequestDTO(string Email, string Senha);

/// <summary>Corpo do pedido de criação de conta.</summary>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Email">E-mail, que também é o login.</param>
/// <param name="Senha">Senha.</param>
public sealed record RegistrarRequestDTO(string Nome, string Email, string Senha);

/// <summary>
/// Corpo dos pedidos de renovação e de logout.
/// </summary>
/// <remarks>
/// Opcional: com <c>CookieDeSessao:Habilitado</c> ligado — o padrão — o token vem do cookie e o
/// corpo é ignorado. Continua existindo para o cliente que não é navegador.
/// </remarks>
/// <remarks>
/// <b>O campo é anulável de propósito.</b> Sendo <c>string</c> não-anulável, a validação
/// automática do <c>[ApiController]</c> o trata como obrigatório e responde 400 a um corpo
/// <c>{}</c> — que é exatamente o que o cliente manda no modo cookie, onde não há token a
/// enviar.
/// </remarks>
/// <param name="RefreshToken">Refresh token recebido no login. Nulo no modo cookie.</param>
public sealed record RefreshRequestDTO(string? RefreshToken);

/// <summary>
/// Par de tokens devolvido no login e na renovação.
/// </summary>
/// <remarks>
/// <b><see cref="RefreshToken"/> vem nulo quando o cookie de sessão está ligado</b>, e isso é o
/// ponto da funcionalidade: devolvê-lo aqui anularia o <c>HttpOnly</c>, porque bastaria um XSS
/// chamar <c>/auth/refresh</c> e ler o token novo da resposta.
/// </remarks>
/// <param name="AccessToken">Enviar em <c>Authorization: Bearer &lt;token&gt;</c>.</param>
/// <param name="ExpiraEm">Expiração do access token, em UTC (ISO 8601).</param>
/// <param name="RefreshToken">Nulo no modo cookie. Fora dele, usar para obter o próximo par.</param>
public sealed record TokenResponseDTO(string AccessToken, DateTime ExpiraEm, string? RefreshToken);
