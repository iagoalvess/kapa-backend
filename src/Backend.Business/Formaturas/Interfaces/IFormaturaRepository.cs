using Backend.Business.Formaturas.Models;

namespace Backend.Business.Formaturas.Interfaces;

/// <summary>
/// Consultas e escrita da formatura em si.
/// </summary>
/// <remarks>
/// Recebe o id explícito, e não lê da sessão: <see cref="Formatura"/> é a raiz do isolamento e
/// fica fora do filtro global. Quem garante que o id é da sessão é o controller, que o tira da claim.
/// </remarks>
public interface IFormaturaRepository
{
    /// <summary>Detalhe da formatura, sem rastreamento.</summary>
    /// <param name="formaturaId">Formatura consultada.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<FormaturaDetalhe?> ObterDetalhe(Guid formaturaId, CancellationToken ct = default);

    /// <summary>Status atual, ou nulo se a formatura não existir. É a consulta da política de escrita.</summary>
    /// <param name="formaturaId">Formatura consultada.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<StatusDaFormatura?> ObterStatus(Guid formaturaId, CancellationToken ct = default);

    /// <summary>A formatura, rastreada para alteração.</summary>
    /// <param name="formaturaId">Formatura a alterar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Formatura?> ObterParaEdicao(Guid formaturaId, CancellationToken ct = default);

    /// <summary>Se o usuário já criou uma formatura que continua em rascunho.</summary>
    /// <param name="usuarioId">Criador.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<bool> ExisteRascunhoCriadoPor(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Registra uma formatura nova.</summary>
    /// <param name="formatura">Formatura a persistir.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task Adicionar(Formatura formatura, CancellationToken ct = default);
}
