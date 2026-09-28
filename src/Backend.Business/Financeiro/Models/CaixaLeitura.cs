namespace Backend.Business.Financeiro.Models;

/// <summary>
/// Quanto a turma tem, hoje.
/// </summary>
/// <remarks>
/// Tudo aqui é agregação de lançamento (decisão 1): <see cref="SaldoEmCentavos"/> é
/// <see cref="ArrecadadoEmCentavos"/> menos <see cref="GastoEmCentavos"/>, calculado na consulta.
/// Não existe coluna de saldo — <c>grep -rn "SaldoEmCaixa" src/</c> não acha nada, e é de propósito.
/// </remarks>
/// <param name="ArrecadadoEmCentavos">O que entrou: recebimentos não estornados e receitas recebidas (Sprint 28).</param>
/// <param name="GastoEmCentavos">O que saiu: despesas pagas.</param>
/// <param name="AReceberEmCentavos">Parcelas em aberto que ainda vencem — o vencido fica fora (decisão 6).</param>
/// <param name="EmAtrasoEmCentavos">Parcelas vencidas e não pagas, pelo valor original.</param>
/// <param name="APagarEmCentavos">Despesas previstas, atrasadas incluídas.</param>
/// <param name="PorCategoria">O quadro por categoria, do maior gasto para o menor.</param>
/// <param name="OutrasReceitasPorCategoria">O quadro das receitas que não vêm de formando, da maior para a menor.</param>
/// <param name="Ultimos">Os últimos lançamentos, entradas e saídas misturadas, do mais recente.</param>
public sealed record CaixaConsolidado(
    long ArrecadadoEmCentavos,
    long GastoEmCentavos,
    long AReceberEmCentavos,
    long EmAtrasoEmCentavos,
    long APagarEmCentavos,
    IReadOnlyList<GastoPorCategoria> PorCategoria,
    IReadOnlyList<OutraReceitaPorCategoria> OutrasReceitasPorCategoria,
    IReadOnlyList<LancamentoDoCaixa> Ultimos
)
{
    /// <summary>O que a turma tem: o que entrou menos o que saiu. Pode ser negativo.</summary>
    public long SaldoEmCentavos => ArrecadadoEmCentavos - GastoEmCentavos;

    /// <summary>O que sobra se todo mundo pagar e todas as contas forem pagas.</summary>
    public long SaldoProjetadoEmCentavos => SaldoEmCentavos + AReceberEmCentavos - APagarEmCentavos;
}

/// <summary>Quanto a turma gastou numa categoria.</summary>
/// <param name="Categoria">Categoria.</param>
/// <param name="Quantidade">Despesas lançadas, canceladas de fora.</param>
/// <param name="PagoEmCentavos">O que já saiu.</param>
/// <param name="PrevistoEmCentavos">O que ainda vai sair.</param>
public sealed record GastoPorCategoria(CategoriaDeDespesa Categoria, int Quantidade, long PagoEmCentavos, long PrevistoEmCentavos);

/// <summary>Quanto entrou — ou ainda vai entrar — numa categoria de receita.</summary>
/// <param name="Categoria">Categoria.</param>
/// <param name="Quantidade">Receitas lançadas, canceladas de fora.</param>
/// <param name="RecebidoEmCentavos">O que já entrou.</param>
/// <param name="PrevistoEmCentavos">O que ainda vai entrar — informativo, nunca somado ao arrecadado (P2).</param>
public sealed record OutraReceitaPorCategoria(CategoriaDeOutraReceita Categoria, int Quantidade, long RecebidoEmCentavos, long PrevistoEmCentavos);

/// <summary>Uma linha do extrato do caixa da turma.</summary>
/// <param name="Data">Dia em que o dinheiro se moveu.</param>
/// <param name="Descricao">O que foi: "Pagamento de parcela", ou a descrição da despesa ou da receita.</param>
/// <param name="ValorEmCentavos">Valor, sempre positivo — o sinal está em <paramref name="Entrada"/>.</param>
/// <param name="Entrada">Entrada de dinheiro; falso, saída.</param>
public sealed record LancamentoDoCaixa(DateOnly Data, string Descricao, long ValorEmCentavos, bool Entrada);

/// <summary>
/// O caixa mês a mês: o realizado até hoje e o projetado daqui para a frente.
/// </summary>
/// <remarks>
/// A projeção conta só o que ainda vence (decisão 6, de 14/09/2026): parcela vencida e não paga não
/// entra em mês nenhum — vai em <see cref="EmAtrasoEmCentavos"/>, à parte. Despesa prevista atrasada
/// entra no mês atual, porque o dinheiro ainda vai sair.
/// </remarks>
/// <param name="Meses">Do primeiro mês com movimento até a colação, sem buraco.</param>
/// <param name="SaldoEmCentavos">O saldo de hoje — de onde a linha projetada parte.</param>
/// <param name="EmAtrasoEmCentavos">O que está vencido e não entrou em mês nenhum.</param>
public sealed record ProjecaoDoCaixa(IReadOnlyList<MesDoCaixa> Meses, long SaldoEmCentavos, long EmAtrasoEmCentavos);

/// <summary>Um mês do fluxo de caixa.</summary>
/// <param name="Mes">Primeiro dia do mês.</param>
/// <param name="EntradasEmCentavos">O que entrou de fato: parcelas e receitas.</param>
/// <param name="SaidasEmCentavos">O que saiu de fato.</param>
/// <param name="EntradasPrevistasEmCentavos">Parcelas que vencem no mês e ainda não foram pagas, mais as receitas previstas para ele.</param>
/// <param name="SaidasPrevistasEmCentavos">Despesas previstas que vencem no mês.</param>
/// <param name="SaldoAcumuladoEmCentavos">Saldo ao fim do mês, somando realizado e previsto.</param>
/// <param name="Projetado">Mês no futuro — a tela o desenha pontilhado e o rotula como projeção.</param>
public sealed record MesDoCaixa(
    DateOnly Mes,
    long EntradasEmCentavos,
    long SaidasEmCentavos,
    long EntradasPrevistasEmCentavos,
    long SaidasPrevistasEmCentavos,
    long SaldoAcumuladoEmCentavos,
    bool Projetado
);

/// <summary>O total juntado pela turma ao fim de um mês.</summary>
/// <param name="Mes">Primeiro dia do mês.</param>
/// <param name="ArrecadadoEmCentavos">Tudo o que entrou desde o começo até o fim do mês — acumulado.</param>
/// <param name="Projetado">Mês no futuro: o que já entrou mais o que vence nele.</param>
public sealed record MesDaArrecadacao(DateOnly Mes, long ArrecadadoEmCentavos, bool Projetado);

/// <summary>Um mês com um valor, como sai da consulta agrupada.</summary>
/// <param name="Mes">Primeiro dia do mês.</param>
/// <param name="ValorEmCentavos">Soma do mês.</param>
public sealed record SomaDoMes(DateOnly Mes, long ValorEmCentavos);
