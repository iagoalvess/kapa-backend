namespace Backend.Business.Cobrancas.Models;

/// <summary>
/// Situação de uma parcela.
/// </summary>
/// <remarks>
/// <see cref="Vencida"/> não é gravado: é <see cref="Aberta"/> com o vencimento no passado, lido
/// na consulta (<see cref="Parcela.StatusEm"/>). Gravar exigiria um job virando o status toda
/// madrugada — e a parcela ficaria "aberta" pelas horas em que ele não rodou.
/// <para>Gravado como texto, pelo mesmo motivo de <see cref="TipoDeCobranca"/>.</para>
/// </remarks>
public enum StatusDaParcela
{
    /// <summary>A pagar, dentro do prazo.</summary>
    Aberta,

    /// <summary>Pagamento confirmado pela tesouraria (Sprint 9).</summary>
    Paga,

    /// <summary>Aberta e com o vencimento no passado. Calculado, nunca gravado.</summary>
    Vencida,

    /// <summary>Deixou de ser devida: o item foi encerrado antes de ela vencer.</summary>
    Cancelada,

    /// <summary>Substituída por um acordo (pós-lançamento).</summary>
    Renegociada,
}
