using Backend.Business.Abstractions;
using Backend.Business.Auth.Models;
using Backend.Business.Usuarios.Models;

namespace Backend.Business.Auth.Interfaces;

/// <summary>
/// Ciclo de vida da conta: confirmação de e-mail e senha.
/// </summary>
/// <remarks>
/// Separado do <see cref="IAuthService"/> por responsabilidade: lá é sessão — entrar, renovar,
/// sair. Aqui é a conta em si, que existe independentemente de haver alguém logado.
/// </remarks>
public interface IContaService
{
    /// <summary>
    /// Dispara o e-mail de redefinição de senha.
    /// </summary>
    /// <remarks>
    /// <b>Sempre devolve sucesso</b>, exista a conta ou não. Responder "este e-mail não está
    /// cadastrado" transformaria o endpoint num verificador de quais e-mails têm conta — e ele é
    /// público e anônimo por definição.
    /// </remarks>
    /// <param name="dados">E-mail informado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> SolicitarRedefinicaoDeSenha(PedidoPorEmail dados, CancellationToken ct = default);

    /// <summary>
    /// Redefine a senha a partir do token recebido por e-mail.
    /// </summary>
    /// <remarks>Em caso de sucesso, todas as sessões abertas do usuário são derrubadas.</remarks>
    /// <param name="dados">E-mail, token e nova senha.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> RedefinirSenha(RedefinirSenha dados, CancellationToken ct = default);

    /// <summary>Confirma o e-mail a partir do token recebido.</summary>
    /// <param name="dados">E-mail e token.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> ConfirmarEmail(ConfirmarEmail dados, CancellationToken ct = default);

    /// <summary>Reenvia o e-mail de confirmação. Sempre devolve sucesso, pelo mesmo motivo da redefinição.</summary>
    /// <param name="dados">E-mail informado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> ReenviarConfirmacao(PedidoPorEmail dados, CancellationToken ct = default);

    /// <summary>
    /// Troca a senha de um usuário autenticado, exigindo a senha atual.
    /// </summary>
    /// <remarks>Em caso de sucesso, todas as sessões abertas do usuário são derrubadas.</remarks>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="dados">Senha atual e nova senha.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> AlterarSenha(Guid usuarioId, AlterarSenha dados, CancellationToken ct = default);
}

/// <summary>
/// Monta e enfileira os e-mails do ciclo de vida da conta.
/// </summary>
/// <remarks>
/// Separado do <see cref="IContaService"/> para que o conteúdo das mensagens — que muda por
/// projeto e por identidade visual — fique num lugar só, sem misturar com a regra de negócio.
/// </remarks>
public interface IEmailsDeConta
{
    /// <summary>Enfileira o e-mail de confirmação de endereço.</summary>
    /// <param name="usuario">Destinatário.</param>
    /// <param name="token">Token de confirmação, ainda não codificado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task EnfileirarConfirmacao(Usuario usuario, string token, CancellationToken ct = default);

    /// <summary>Enfileira o e-mail de redefinição de senha.</summary>
    /// <param name="usuario">Destinatário.</param>
    /// <param name="token">Token de redefinição, ainda não codificado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task EnfileirarRedefinicaoDeSenha(Usuario usuario, string token, CancellationToken ct = default);

    /// <summary>
    /// Enfileira o aviso de que a senha foi alterada.
    /// </summary>
    /// <remarks>
    /// Não é cortesia: é o único sinal que o dono legítimo recebe quando alguém tomou a conta e
    /// trocou a senha. Chega depois do fato, mas é a diferença entre descobrir em minutos e
    /// descobrir no mês seguinte.
    /// </remarks>
    /// <param name="usuario">Destinatário.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task EnfileirarAvisoDeSenhaAlterada(Usuario usuario, CancellationToken ct = default);
}
