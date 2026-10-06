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
/// <param name="ReceberComunicacaoDoKapa">Se marcou a caixa de marketing do Kapa (Sprint 40, P1: consentimento).</param>
public sealed record RegistrarUsuario(
    string Nome,
    string Email,
    string Senha,
    IReadOnlyList<AceiteDeDocumento> Aceites,
    bool ReceberComunicacaoDoKapa = false
);

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

/// <summary>
/// O que o login deu: a sessão, ou — para administrador e presidente — o pedido do código que foi ao e-mail
/// (login em duas etapas, revisão de segurança de 05/10/2026).
/// </summary>
/// <param name="Sessao">O par de tokens; nulo enquanto falta o código.</param>
/// <param name="Codigo">O código pedido; nulo quando a sessão já saiu.</param>
public sealed record Entrada(ParDeTokens? Sessao, CodigoDeEntrada? Codigo);

/// <summary>O segundo passo do login: o código foi ao e-mail da conta.</summary>
/// <param name="Desafio">O que o cliente devolve junto do código — liga os dois passos sem sessão.</param>
/// <param name="EnviadoPara">O e-mail, mascarado.</param>
/// <param name="MinutosDeValidade">Por quanto tempo o código vale, para a tela dizer.</param>
public sealed record CodigoDeEntrada(string Desafio, string EnviadoPara, int MinutosDeValidade);

/// <summary>O que viaja assinado no desafio: de quem é o login que espera o código.</summary>
/// <param name="UsuarioId">Quem acertou a senha.</param>
public sealed record DesafioDeEntrada(Guid UsuarioId);
