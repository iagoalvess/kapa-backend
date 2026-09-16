namespace Backend.Business.Financeiro.Models;

/// <summary>
/// Situação de uma despesa.
/// </summary>
/// <remarks>
/// <see cref="Prevista"/> entra no fluxo projetado; <see cref="Paga"/> entra no caixa realizado
/// (decisão 2). "Atrasada" não é status: é <see cref="Prevista"/> com o vencimento no passado, lido
/// na consulta — o mesmo raciocínio de <c>StatusDaParcela.Vencida</c> na Sprint 6.
/// <para>Gravado como texto: o índice único parcial filtra por <c>status &lt;&gt; 'Cancelada'</c>.</para>
/// </remarks>
public enum StatusDaDespesa
{
    /// <summary>Compromisso assumido, ainda não pago.</summary>
    Prevista,

    /// <summary>Dinheiro que saiu, com comprovante (decisão 3).</summary>
    Paga,

    /// <summary>Não vai acontecer: o contrato caiu, a cotação foi outra.</summary>
    Cancelada,
}
