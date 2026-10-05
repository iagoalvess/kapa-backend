using Backend.Business.Cobrancas.Models;
using Backend.Business.Loja.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>As solicitações de cancelamento do formando (Sprint 48, D8).</summary>
public interface ISolicitacaoDeCancelamentoRepository
{
    /// <summary>Se o par (vínculo, item) já tem solicitação esperando a comissão.</summary>
    /// <param name="vinculoId">Formando.</param>
    /// <param name="itemId">Pacote ou item do pedido.</param>
    Task<bool> ExisteAberta(Guid vinculoId, Guid itemId, CancellationToken ct = default);

    /// <summary>A solicitação, rastreada e travada até o fim da transação.</summary>
    /// <param name="id">Solicitação.</param>
    Task<SolicitacaoDeCancelamento?> Travar(Guid id, CancellationToken ct = default);

    /// <summary>Uma solicitação como as telas a mostram.</summary>
    /// <param name="id">Solicitação.</param>
    Task<ResumoDaSolicitacao?> Obter(Guid id, CancellationToken ct = default);

    /// <summary>As solicitações da turma, ou só as de um formando, da mais nova para a mais antiga.</summary>
    /// <param name="vinculoId">Só as deste formando; nulo, todas.</param>
    /// <param name="status">Só nesta situação; nula, todas.</param>
    Task<IReadOnlyList<ResumoDaSolicitacao>> Listar(Guid? vinculoId, StatusDoPedidoDeCancelamento? status, CancellationToken ct = default);

    /// <summary>Marca a solicitação nova para inclusão.</summary>
    /// <param name="solicitacao">Solicitação.</param>
    Task Adicionar(SolicitacaoDeCancelamento solicitacao, CancellationToken ct = default);
}
