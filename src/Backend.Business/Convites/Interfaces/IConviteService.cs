using Backend.Business.Abstractions;
using Backend.Business.Auth.Models;
using Backend.Business.Convites.Models;
using Backend.Business.Legal.Models;

namespace Backend.Business.Convites.Interfaces;

/// <summary>
/// Criação, acompanhamento e aceite de convites.
/// </summary>
/// <remarks>
/// Criar, listar e revogar recebem a formatura da sessão. Consultar e aceitar recebem só o token:
/// é ele que diz qual é a turma.
/// </remarks>
public interface IConviteService
{
    /// <summary>
    /// Cria o convite e, se for nominal, enfileira o e-mail. Devolve o link uma única vez.
    /// </summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Autor, cujo papel decide se pode oferecer papel de comissão.</param>
    /// <param name="dados">E-mail, papel, validade e limite.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ConviteCriado>> Criar(Guid formaturaId, Guid usuarioId, CriarConvite dados, CancellationToken ct = default);

    /// <summary>Os convites mais recentes da formatura da sessão.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<IReadOnlyList<ConviteResumo>>> Listar(CancellationToken ct = default);

    /// <summary>Revoga um convite da formatura da sessão.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Autor da revogação.</param>
    /// <param name="conviteId">Convite.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> Revogar(Guid formaturaId, Guid usuarioId, Guid conviteId, CancellationToken ct = default);

    /// <summary>Turma, instituição e papel de um convite utilizável.</summary>
    /// <param name="token">Token do link.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ConvitePublico>> ObterPublico(string token, CancellationToken ct = default);

    /// <summary>
    /// Vincula a conta autenticada à formatura do convite e devolve a sessão já dentro dela.
    /// </summary>
    /// <param name="usuarioId">Conta que aceita.</param>
    /// <param name="token">Token do link.</param>
    /// <param name="refreshTokenAtual">Refresh token da sessão, rotacionado na troca de formatura.</param>
    /// <param name="origem">IP e navegador, gravados no registro do aceite.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ParDeTokens>> Aceitar(Guid usuarioId, string token, string refreshTokenAtual, OrigemDoAceite origem, CancellationToken ct = default);
}
