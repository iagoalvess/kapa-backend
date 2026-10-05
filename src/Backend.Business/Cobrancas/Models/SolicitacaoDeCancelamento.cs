using Backend.Business.Abstractions;
using Backend.Business.Loja.Models;

namespace Backend.Business.Cobrancas.Models;

/// <summary>
/// O formando pedindo à comissão que cancele um pacote da cesta ou um pedido avulso (Sprint 48, D8).
/// </summary>
/// <remarks>
/// O formando nunca cancela sozinho, nem sem nada pago: abre esta solicitação, e a comissão aprova ou recusa, com
/// motivo e auditoria — o mesmo desenho do pedido de cancelamento da loja (Sprint 38), de onde vem o
/// <see cref="StatusDoPedidoDeCancelamento"/>.
/// <para>
/// O alvo é o par <c>(vínculo, item)</c>: o pedido é um por item por formando (Sprint 20, decisão 3) e o pacote
/// entra uma vez na cesta, então o item basta para achar os dois — e as parcelas, que têm a mesma chave. Uma aberta
/// por par, garantido pelo índice único parcial.
/// </para>
/// <para>
/// Enquanto aberta, as parcelas em aberto do item ficam suspensas até <see cref="RespostaAte"/> (D12/D37);
/// respondida, voltam a cobrar.
/// </para>
/// </remarks>
public class SolicitacaoDeCancelamento : EntidadeDaFormatura
{
    /// <summary>Quantos dias a comissão tem para responder antes de a cobrança voltar (D37).</summary>
    public const int DiasParaResponder = 7;

    /// <summary>Quem pediu.</summary>
    public Guid VinculoId { get; private set; }

    /// <summary>O pacote ou o item do pedido.</summary>
    public Guid ItemDeCobrancaId { get; private set; }

    /// <summary>O pedido avulso, se for um; nulo é pacote da cesta.</summary>
    public Guid? PedidoId { get; private set; }

    /// <summary>Por que, se o formando disse.</summary>
    public string? Motivo { get; private set; }

    /// <summary>Quando pediu, em UTC.</summary>
    public DateTime PedidoEm { get; private set; }

    /// <summary>Último dia do prazo de resposta — e da suspensão das parcelas.</summary>
    public DateOnly RespostaAte { get; private set; }

    /// <summary>Situação.</summary>
    public StatusDoPedidoDeCancelamento Status { get; private set; } = StatusDoPedidoDeCancelamento.Aberto;

    /// <summary>Quem da comissão respondeu.</summary>
    public Guid? RespondidoPorUsuarioId { get; private set; }

    /// <summary>Quando respondeu, em UTC.</summary>
    public DateTime? RespondidoEm { get; private set; }

    /// <summary>O motivo da recusa — o formando o lê na tela.</summary>
    public string? MotivoDaResposta { get; private set; }

    /// <summary>Construtor do EF.</summary>
    protected SolicitacaoDeCancelamento() { }

    /// <summary>Uma solicitação nova, aberta, com o prazo de resposta contado de hoje.</summary>
    /// <param name="vinculoId">Quem pede.</param>
    /// <param name="itemDeCobrancaId">Pacote ou item do pedido.</param>
    /// <param name="pedidoId">O pedido, se for avulso.</param>
    /// <param name="motivo">Por que, se disse.</param>
    /// <param name="agoraUtc">Agora.</param>
    /// <param name="hoje">Dia de hoje, no fuso da turma.</param>
    public SolicitacaoDeCancelamento(Guid vinculoId, Guid itemDeCobrancaId, Guid? pedidoId, string? motivo, DateTime agoraUtc, DateOnly hoje)
    {
        VinculoId = vinculoId;
        ItemDeCobrancaId = itemDeCobrancaId;
        PedidoId = pedidoId;
        Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        PedidoEm = agoraUtc;
        RespostaAte = hoje.AddDays(DiasParaResponder);
    }

    /// <summary>A resposta da comissão. Só a aberta responde.</summary>
    /// <param name="aprovada">Se aprovou.</param>
    /// <param name="usuarioId">Quem respondeu.</param>
    /// <param name="motivo">O motivo da recusa.</param>
    /// <param name="agoraUtc">Agora.</param>
    /// <returns>Se respondeu agora — falso quando outro já tinha respondido.</returns>
    public bool Responder(bool aprovada, Guid usuarioId, string? motivo, DateTime agoraUtc)
    {
        if (Status != StatusDoPedidoDeCancelamento.Aberto)
            return false;

        Status = aprovada ? StatusDoPedidoDeCancelamento.Aprovado : StatusDoPedidoDeCancelamento.Recusado;
        RespondidoPorUsuarioId = usuarioId;
        RespondidoEm = agoraUtc;
        MotivoDaResposta = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();

        return true;
    }
}
