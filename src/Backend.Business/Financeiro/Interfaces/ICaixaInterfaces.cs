using Backend.Business.Abstractions;
using Backend.Business.Financeiro.Models;

namespace Backend.Business.Financeiro.Interfaces;

/// <summary>
/// Quanto a turma tem, quanto ainda entra e quanto ainda sai.
/// </summary>
/// <remarks>
/// Leitura pura: nada aqui grava. Saldo é agregação, nunca coluna (decisão 1).
/// </remarks>
public interface ICaixaService
{
    /// <summary>O consolidado de hoje: arrecadado, gasto, saldo, a receber, o quadro por categoria e os últimos lançamentos.</summary>
    Task<Result<CaixaConsolidado>> Consolidado(CancellationToken ct = default);

    /// <summary>O fluxo mês a mês: realizado até hoje, projetado até a colação.</summary>
    /// <param name="formaturaId">Formatura da sessão — é dela a data da colação.</param>
    Task<Result<ProjecaoDoCaixa>> Projecao(Guid formaturaId, bool soRealizado = false, CancellationToken ct = default);

    /// <summary>
    /// Quanto a turma já tinha juntado ao fim de cada um dos últimos meses, e quanto deve ter no próximo.
    /// </summary>
    /// <remarks>
    /// É o gráfico do Início, e todo membro o lê: só entradas, somadas — nem despesa nem planejamento,
    /// que continuam na projeção da Gestão.
    /// </remarks>
    Task<Result<IReadOnlyList<MesDaArrecadacao>>> Arrecadacao(CancellationToken ct = default);
}

/// <summary>
/// As agregações do caixa da formatura selecionada.
/// </summary>
/// <remarks>
/// Atravessa quatro agregados — recebimento (Sprint 9), parcela (Sprint 6), despesa e receita
/// (Sprint 28) —, e é o único
/// lugar onde isso acontece: caixa é relatório, e relatório que respeita fronteira de agregado vira
/// três consultas e uma soma em memória. Cada método é um <c>GROUP BY</c> só, com o filtro global
/// aplicado pelo contexto — e quando soma parcela e receita, as duas origens vão na mesma consulta
/// (<c>UNION ALL</c>), nunca em duas idas ao banco somadas em memória.
/// </remarks>
public interface ICaixaRepository
{
    /// <summary>O que entrou: recebimentos não estornados mais receitas recebidas, numa consulta.</summary>
    /// <remarks>Receita prevista não entra (P2 da Sprint 28): este número é o do dashboard e o da meta da festa.</remarks>
    Task<long> Arrecadado(CancellationToken ct = default);

    /// <summary>O que saiu: soma das despesas pagas.</summary>
    Task<long> Gasto(CancellationToken ct = default);

    /// <summary>Parcelas em aberto, separadas entre as que ainda vencem e as vencidas.</summary>
    /// <param name="hoje">Dia que separa a vencer de vencida.</param>
    Task<(long AVencer, long EmAtraso)> ParcelasEmAberto(DateOnly hoje, CancellationToken ct = default);

    /// <summary>Despesas previstas, atrasadas incluídas.</summary>
    Task<long> DespesasPrevistas(CancellationToken ct = default);

    /// <summary>O gasto por categoria, do maior para o menor.</summary>
    Task<IReadOnlyList<GastoPorCategoria>> PorCategoria(CancellationToken ct = default);

    /// <summary>O quadro das receitas por categoria, da maior para a menor.</summary>
    Task<IReadOnlyList<OutraReceitaPorCategoria>> OutrasReceitasPorCategoria(CancellationToken ct = default);

    /// <summary>Os últimos lançamentos — entradas e saídas —, do mais recente.</summary>
    /// <param name="quantidade">Quantas linhas.</param>
    Task<IReadOnlyList<LancamentoDoCaixa>> UltimosLancamentos(int quantidade, CancellationToken ct = default);

    /// <summary>Recebimentos e receitas recebidas, por mês em que entraram.</summary>
    Task<IReadOnlyList<SomaDoMes>> EntradasPorMes(CancellationToken ct = default);

    /// <summary>Despesas pagas por mês do pagamento.</summary>
    Task<IReadOnlyList<SomaDoMes>> SaidasPorMes(CancellationToken ct = default);

    /// <summary>Parcelas em aberto que ainda vencem, por mês do vencimento — e, se pedido, as receitas previstas.</summary>
    /// <param name="hoje">Dia que separa a vencer de vencida; o vencido fica fora (decisão 6).</param>
    /// <param name="comOutrasReceitasPrevistas">
    /// Soma as receitas previstas que ainda vão entrar. Só a projeção do caixa pede (P2 da Sprint 28):
    /// o gráfico do Início é lido pela turma inteira, e patrocínio prometido não pode aparecer ali.
    /// </param>
    Task<IReadOnlyList<SomaDoMes>> EntradasPrevistasPorMes(DateOnly hoje, bool comOutrasReceitasPrevistas, CancellationToken ct = default);

    /// <summary>Despesas previstas por mês do vencimento; as atrasadas caem no mês de hoje.</summary>
    /// <param name="hoje">Dia que separa prevista de atrasada.</param>
    Task<IReadOnlyList<SomaDoMes>> SaidasPrevistasPorMes(DateOnly hoje, CancellationToken ct = default);
}
