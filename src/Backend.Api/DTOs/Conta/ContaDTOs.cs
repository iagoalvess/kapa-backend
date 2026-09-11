namespace Backend.Api.DTOs.Conta;

/// <summary>Corpo do pedido de redefinição de senha ou de reenvio de confirmação.</summary>
/// <param name="Email">E-mail da conta.</param>
public sealed record PedidoPorEmailRequestDTO(string Email);

/// <summary>Corpo do consumo do link de redefinição de senha.</summary>
/// <param name="Email">E-mail da conta, como veio no link.</param>
/// <param name="Token">Token recebido no link, sem modificação.</param>
/// <param name="NovaSenha">Nova senha.</param>
public sealed record RedefinirSenhaRequestDTO(string Email, string Token, string NovaSenha);

/// <summary>Corpo do consumo do link de confirmação de e-mail.</summary>
/// <param name="Email">E-mail da conta, como veio no link.</param>
/// <param name="Token">Token recebido no link, sem modificação.</param>
public sealed record ConfirmarEmailRequestDTO(string Email, string Token);

/// <summary>Corpo da troca de senha por um usuário autenticado.</summary>
/// <param name="SenhaAtual">Senha em uso.</param>
/// <param name="NovaSenha">Nova senha.</param>
public sealed record AlterarSenhaRequestDTO(string SenhaAtual, string NovaSenha);
