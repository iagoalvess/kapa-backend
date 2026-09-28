namespace Backend.Business.Financeiro.Models;

/// <summary>
/// Situação de uma receita — o espelho de <see cref="StatusDaDespesa"/>.
/// </summary>
/// <remarks>
/// <see cref="Prevista"/> entra <b>só</b> na projeção do caixa, nunca no arrecadado, no dashboard nem
/// na meta da festa (P2, respondida em 23/09/2026): patrocínio prometido e não pago não pode inflar a
/// tela que a turma inteira lê. "Atrasada" não é status: é prevista com a data no passado.
/// <para>Gravado como texto: o índice único parcial filtra por <c>status &lt;&gt; 'Cancelada'</c>.</para>
/// </remarks>
public enum StatusDaOutraReceita
{
    /// <summary>Combinada, ainda não caiu na conta.</summary>
    Prevista,

    /// <summary>Dinheiro que entrou — conta no arrecadado, no saldo e na meta.</summary>
    Recebida,

    /// <summary>Não vai acontecer: o patrocinador desistiu, o evento não saiu.</summary>
    Cancelada,
}
