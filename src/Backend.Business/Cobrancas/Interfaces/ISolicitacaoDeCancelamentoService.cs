using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Loja.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// O cancelamento de pacote e de pedido como solicitação à comissão (Sprint 48, D8).
/// </summary>
/// <remarks>
/// O formando pede; a tesouraria aprova ou recusa. Aprovar cancela o pacote ou o pedido inteiro e leva o já pago à
/// lista "a devolver" (D9); recusar devolve a cobrança. Responder, de um jeito ou de outro, tira a suspensão das
/// parcelas (D12).
/// </remarks>
public interface ISolicitacaoDeCancelamentoService
{
    /// <summary>Pede o cancelamento de um pacote da cesta ou de um pedido avulso.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    /// <param name="dados">O item e o motivo.</param>
    Task<Result<ResumoDaSolicitacao>> Solicitar(Guid formaturaId, Guid usuarioId, DadosDaSolicitacao dados, CancellationToken ct = default);

    /// <summary>As solicitações do próprio formando, da mais nova para a mais antiga.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">O próprio.</param>
    Task<Result<IReadOnlyList<ResumoDaSolicitacao>>> ListarMinhas(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>As solicitações da turma — a fila da comissão.</summary>
    /// <param name="status">Só nesta situação; nula, todas.</param>
    Task<Result<IReadOnlyList<ResumoDaSolicitacao>>> Listar(StatusDoPedidoDeCancelamento? status, CancellationToken ct = default);

    /// <summary>Aprova: cancela o pacote ou o pedido, e o pago vai para "a devolver".</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="solicitacaoId">Solicitação.</param>
    /// <param name="usuarioId">Quem aprova — da tesouraria.</param>
    Task<Result<ResumoDaSolicitacao>> Aprovar(Guid formaturaId, Guid solicitacaoId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Recusa, com motivo: a cobrança volta.</summary>
    /// <param name="solicitacaoId">Solicitação.</param>
    /// <param name="motivo">Por que — o formando lê.</param>
    /// <param name="usuarioId">Quem recusa.</param>
    Task<Result<ResumoDaSolicitacao>> Recusar(Guid solicitacaoId, string motivo, Guid usuarioId, CancellationToken ct = default);
}
