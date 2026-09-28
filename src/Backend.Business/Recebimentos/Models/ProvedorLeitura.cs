namespace Backend.Business.Recebimentos.Models;

/// <summary>
/// O Mercado Pago da turma, como a tela o mostra: nunca o token, só o que basta para reconhecer a conta.
/// </summary>
/// <param name="ContaNoProvedor">E-mail ou apelido da conta que autorizou.</param>
/// <param name="ConectadoEm">Quando a autorização atual foi dada, em UTC.</param>
/// <param name="ConectadoPor">Nome de quem autorizou.</param>
public sealed record ProvedorConectado(string ContaNoProvedor, DateTime ConectadoEm, string? ConectadoPor);

/// <summary>O Mercado Pago da turma; nulo enquanto ela não conectou.</summary>
/// <param name="Provedor">A conexão, ou nulo.</param>
public sealed record ProvedorDaTurma(ProvedorConectado? Provedor);

/// <summary>Para onde o navegador do presidente vai para autorizar o Kapa no Mercado Pago.</summary>
/// <param name="Url">A página de autorização, com o <c>state</c> assinado.</param>
public sealed record AutorizacaoDoProvedor(string Url);

/// <summary>O PIX do Mercado Pago da turma pronto para a tela do formando.</summary>
/// <param name="CopiaECola">O BR Code, que a tela transforma em QR.</param>
/// <param name="ExpiraEm">Até quando aceita pagamento, em UTC.</param>
public sealed record PixDinamicoParaPagar(string CopiaECola, DateTime ExpiraEm);
