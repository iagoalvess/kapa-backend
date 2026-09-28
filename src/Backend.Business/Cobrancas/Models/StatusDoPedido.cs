namespace Backend.Business.Cobrancas.Models;

/// <summary>
/// Situação de um pedido do formando.
/// </summary>
/// <remarks>
/// Só dois valores, e é a P2 respondida em 22/09/2026: o pedido não passa por aprovação da
/// comissão. Não existe <c>Solicitado</c> nem <c>Recusado</c> porque os três medos que a aprovação
/// resolvia já têm dono melhor — o estoque, a cota por formando e a data de abertura das vendas.
/// <para>Gravado como texto, pelo mesmo motivo de <see cref="StatusDaParcela"/>.</para>
/// </remarks>
public enum StatusDoPedido
{
    /// <summary>De pé: ocupa estoque e as parcelas dele estão no extrato do formando.</summary>
    Confirmado,

    /// <summary>Desfeito: o estoque voltou e as parcelas em aberto foram canceladas.</summary>
    Cancelado,
}
