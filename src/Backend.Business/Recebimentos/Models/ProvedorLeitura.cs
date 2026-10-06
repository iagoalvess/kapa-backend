namespace Backend.Business.Recebimentos.Models;

/// <summary>
/// O Mercado Pago da turma, como a tela o mostra: nunca o token, só o que basta para reconhecer a conta.
/// </summary>
/// <param name="ContaNoProvedor">E-mail ou apelido da conta que autorizou.</param>
/// <param name="ConectadoEm">Quando a autorização atual foi dada, em UTC.</param>
/// <param name="ConectadoPor">Nome de quem autorizou.</param>
/// <param name="Cartao">O cartão da turma (Sprint 39): se ligou, quando, por quem e com que taxa.</param>
/// <param name="CobrancaAutomaticaEm">Desde quando as parcelas se pagam só pelo Mercado Pago, em UTC; nulo: modo manual.</param>
public sealed record ProvedorConectado(
    string ContaNoProvedor,
    DateTime ConectadoEm,
    string? ConectadoPor,
    CartaoDaTurma Cartao,
    DateTime? CobrancaAutomaticaEm
);

/// <summary>A troca do modo de cobrança das parcelas.</summary>
/// <param name="Automatica">Verdadeiro: só Mercado Pago; falso: só os meios da comissão, com aviso e conferência.</param>
public sealed record ModoDeCobranca(bool Automatica);

/// <summary>O cartão da turma como a tela o mostra (Sprint 39, P2 e P7).</summary>
/// <param name="Disponivel">Se a conexão tem a chave pública; sem ela, o presidente conecta de novo antes de ligar.</param>
/// <param name="LigadoEm">Quando foi ligado, em UTC; nulo, desligado.</param>
/// <param name="LigadoPor">Nome de quem ligou.</param>
/// <param name="TaxaRepassada">A taxa repassada ao formando, base 10.000; nula, a turma absorve.</param>
public sealed record CartaoDaTurma(bool Disponivel, DateTime? LigadoEm, string? LigadoPor, int? TaxaRepassada);

/// <summary>O que a Tesouraria escolhe ao ligar ou desligar o cartão.</summary>
/// <param name="Ligado">Liga ou desliga.</param>
/// <param name="TaxaRepassada">Base 10.000, entre 0,01% e 15%; nula, a turma absorve a taxa (o padrão).</param>
public sealed record ConfiguracaoDoCartao(bool Ligado, int? TaxaRepassada);

/// <summary>
/// O cartão pronto para a tela de pagar: com o que o formulário do Mercado Pago precisa e o valor que ele cobra.
/// </summary>
/// <remarks>O número do cartão nunca passa pelo Kapa: o navegador tokeniza com a chave pública da conta da turma.</remarks>
/// <param name="ChavePublica">A <c>public_key</c> da conta da turma.</param>
/// <param name="ValorEmCentavos">O que o cartão cobra — o valor do PIX mais o acréscimo.</param>
/// <param name="AcrescimoEmCentavos">A taxa repassada ao formando (P2); zero quando a turma absorve.</param>
/// <param name="MaximoDeParcelas">Em quantas vezes, no máximo — os juros são de quem paga (P3).</param>
public sealed record CartaoParaPagar(string ChavePublica, long ValorEmCentavos, long AcrescimoEmCentavos, int MaximoDeParcelas);

/// <summary>O Mercado Pago da turma; nulo enquanto ela não conectou.</summary>
/// <param name="Provedor">A conexão, ou nulo.</param>
public sealed record ProvedorDaTurma(ProvedorConectado? Provedor);

/// <summary>O link de autorização do Mercado Pago foi para o e-mail de quem clicou em Conectar.</summary>
/// <param name="EnviadaPara">O e-mail, mascarado.</param>
public sealed record AutorizacaoDoProvedor(string EnviadaPara);

/// <summary>O PIX do Mercado Pago da turma pronto para a tela do formando.</summary>
/// <param name="CopiaECola">O BR Code, que a tela transforma em QR.</param>
/// <param name="ExpiraEm">Até quando aceita pagamento, em UTC.</param>
public sealed record PixDinamicoParaPagar(string CopiaECola, DateTime ExpiraEm);
