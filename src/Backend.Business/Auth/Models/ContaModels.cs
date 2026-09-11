namespace Backend.Business.Auth.Models;

/// <summary>Pedido de redefinição de senha ou de reenvio de confirmação.</summary>
/// <param name="Email">E-mail da conta.</param>
public sealed record PedidoPorEmail(string Email);

/// <summary>Consumo do link de redefinição de senha.</summary>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Token">Token recebido no link, como ele veio.</param>
/// <param name="NovaSenha">Nova senha.</param>
public sealed record RedefinirSenha(string Email, string Token, string NovaSenha);

/// <summary>Consumo do link de confirmação de e-mail.</summary>
/// <param name="Email">E-mail da conta.</param>
/// <param name="Token">Token recebido no link, como ele veio.</param>
public sealed record ConfirmarEmail(string Email, string Token);

/// <summary>Troca de senha por um usuário autenticado.</summary>
/// <param name="SenhaAtual">Senha em uso, exigida para provar posse da sessão.</param>
/// <param name="NovaSenha">Nova senha.</param>
public sealed record AlterarSenha(string SenhaAtual, string NovaSenha);
