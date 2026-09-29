using Backend.Business.Abstractions;

namespace Backend.Business.Loja.Models;

/// <summary>Em que ponto está o pedido de cancelamento do comprador. Gravado como texto.</summary>
public enum StatusDoPedidoDeCancelamento
{
    /// <summary>Esperando a Gestão. Os convites continuam valendo.</summary>
    Aberto,

    /// <summary>A Gestão aprovou: os convites foram cancelados e a compra foi para a lista a devolver.</summary>
    Aprovado,

    /// <summary>A Gestão recusou, com motivo. Os convites continuam valendo.</summary>
    Recusado,
}

/// <summary>
/// O comprador pedindo, pelo link da compra, que a turma cancele convites e devolva o dinheiro (Sprint 38, P1).
/// </summary>
/// <remarks>
/// É um pedido, não um cancelamento (decisão 5): enquanto aberto, os convites valem, e quem desistiu de desistir
/// ainda entra. Guarda a data do pedido — é o que importa se o prazo de arrependimento do CDC valer (P2). Um
/// aberto por compra, garantido pelo índice único parcial; aprovar passa pelo mesmo cancelamento da Gestão.
/// </remarks>
public class PedidoDeCancelamento : EntidadeDaFormatura
{
    /// <summary>A compra.</summary>
    public Guid CompraId { get; private set; }

    /// <summary>Os convites que o comprador quer cancelar.</summary>
    public Guid[] ConviteIds { get; private set; } = [];

    /// <summary>Por que, se ele disse.</summary>
    public string? Motivo { get; private set; }

    /// <summary>Quando pediu, em UTC.</summary>
    public DateTime PedidoEm { get; private set; }

    /// <summary>Situação.</summary>
    public StatusDoPedidoDeCancelamento Status { get; private set; } = StatusDoPedidoDeCancelamento.Aberto;

    /// <summary>Quem da Gestão respondeu.</summary>
    public Guid? RespondidoPorUsuarioId { get; private set; }

    /// <summary>Quando a Gestão respondeu, em UTC.</summary>
    public DateTime? RespondidoEm { get; private set; }

    /// <summary>O motivo da recusa — o comprador o lê no link e no e-mail.</summary>
    public string? MotivoDaResposta { get; private set; }

    /// <summary>Construtor do EF.</summary>
    protected PedidoDeCancelamento() { }

    /// <summary>Um pedido novo, aberto.</summary>
    /// <param name="compraId">A compra.</param>
    /// <param name="conviteIds">Os convites, já conferidos como válidos e desta compra.</param>
    /// <param name="motivo">Por que, se ele disse.</param>
    /// <param name="agora">Instante, em UTC.</param>
    public PedidoDeCancelamento(Guid compraId, IEnumerable<Guid> conviteIds, string? motivo, DateTime agora)
    {
        CompraId = compraId;
        ConviteIds = [.. conviteIds.Distinct()];
        Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        PedidoEm = agora;
    }

    /// <summary>A resposta da Gestão. Só o aberto responde.</summary>
    /// <param name="aprovado">Se aprovou.</param>
    /// <param name="usuarioId">Quem respondeu.</param>
    /// <param name="motivo">O motivo da recusa.</param>
    /// <param name="agora">Instante, em UTC.</param>
    /// <returns>Se respondeu agora — falso quando outro já tinha respondido.</returns>
    public bool Responder(bool aprovado, Guid usuarioId, string? motivo, DateTime agora)
    {
        if (Status != StatusDoPedidoDeCancelamento.Aberto)
            return false;

        Status = aprovado ? StatusDoPedidoDeCancelamento.Aprovado : StatusDoPedidoDeCancelamento.Recusado;
        RespondidoPorUsuarioId = usuarioId;
        RespondidoEm = agora;
        MotivoDaResposta = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();

        return true;
    }
}
