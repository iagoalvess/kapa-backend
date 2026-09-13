namespace Backend.Business.Assinaturas.Models;

/// <summary>
/// Situação da assinatura da licença.
/// </summary>
/// <remarks>Gravado como texto, pelo mesmo motivo de <c>StatusDaFormatura</c>.</remarks>
public enum StatusDaAssinatura
{
    /// <summary>Checkout iniciado; o provedor ainda não confirmou o pagamento.</summary>
    Pendente,

    /// <summary>Paga e dentro da vigência (ou da carência).</summary>
    Ativa,

    /// <summary>Vigência e carência acabaram sem renovação. A formatura fica suspensa.</summary>
    Vencida,

    /// <summary>Renovação cancelada. A vigência corrente é respeitada até o fim.</summary>
    Cancelada,
}
