namespace Backend.Business.Financeiro.Models;

/// <summary>
/// De onde vem o dinheiro que não é parcela de formando (Sprint 28).
/// </summary>
/// <remarks>
/// Lista fechada (P3, confirmada em 23/09/2026) pelo mesmo motivo de <see cref="CategoriaDeDespesa"/>:
/// categoria livre vira "Patrocínio", "patrocinio" e "Parceria" na mesma turma, e o quadro do caixa
/// deixa de somar. Gravada como texto — renomear um valor aqui é migration, não refatoração.
/// </remarks>
public enum CategoriaDeOutraReceita
{
    /// <summary>A clínica, o laboratório, a loja de becas que paga para ter a marca no palco.</summary>
    Patrocinio,

    /// <summary>Evento de arrecadação: festa junina, bingo, venda de bolo, rifa vendida para fora.</summary>
    Evento,

    /// <summary>Doação de família, de egressos ou da própria instituição.</summary>
    Doacao,

    /// <summary>Rendimento da aplicação, lançado quando o extrato mostra (decisão 5).</summary>
    Rendimento,

    /// <summary>Venda pública de convite (Sprint 26), que não é parcela de ninguém.</summary>
    VendaDeConvite,

    /// <summary>O que não cabe nas demais — a descrição diz o que é.</summary>
    Outros,
}
