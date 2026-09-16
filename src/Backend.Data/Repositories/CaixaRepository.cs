using Backend.Business.Cobrancas.Models;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// As agregações do caixa da formatura selecionada.
/// </summary>
/// <remarks>
/// O único lugar do projeto que lê três agregados na mesma pergunta — recebimento, parcela e despesa.
/// Cada método é uma consulta agregada no banco: nada aqui traz lançamento para a memória para somar
/// em C#, que é como um caixa de 2.000 linhas passa de milissegundos a segundos.
/// <para>
/// Saldo não é consultado: é <see cref="Arrecadado"/> menos <see cref="Gasto"/>, montado no service
/// (decisão 1 da Sprint 10).
/// </para>
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class CaixaRepository(AppDbContext db) : ICaixaRepository
{
    /// <summary>Quem pagou não aparece no caixa: a entrada é um movimento, não uma pessoa.</summary>
    private const string EntradaDeParcela = "Pagamento de parcela";

    /// <inheritdoc />
    public async Task<long> Arrecadado(CancellationToken ct = default) =>
        await db.Recebimentos.AsNoTracking().Where(r => r.EstornadoEm == null).SumAsync(r => (long?)r.ValorEmCentavos, ct) ?? 0;

    /// <inheritdoc />
    public async Task<long> Gasto(CancellationToken ct = default) =>
        await db.Despesas.AsNoTracking().Where(d => d.Status == StatusDaDespesa.Paga).SumAsync(d => (long?)d.ValorEmCentavos, ct) ?? 0;

    /// <inheritdoc />
    /// <remarks>
    /// Pelo valor original, sem multa nem juros: o que cada formando vai pagar a mais depende do dia em
    /// que ele pagar, e projeção não adivinha atraso. Uma consulta, dois grupos.
    /// </remarks>
    public async Task<(long AVencer, long EmAtraso)> ParcelasEmAberto(DateOnly hoje, CancellationToken ct = default)
    {
        var grupos = await db
            .Parcelas.AsNoTracking()
            .Where(p => p.Status == StatusDaParcela.Aberta)
            .GroupBy(p => p.Vencimento < hoje)
            .Select(grupo => new { Atrasada = grupo.Key, Valor = grupo.Sum(p => p.ValorOriginalEmCentavos) })
            .ToListAsync(ct);

        return (grupos.Where(grupo => !grupo.Atrasada).Sum(grupo => grupo.Valor), grupos.Where(grupo => grupo.Atrasada).Sum(grupo => grupo.Valor));
    }

    /// <inheritdoc />
    public async Task<long> DespesasPrevistas(CancellationToken ct = default) =>
        await db.Despesas.AsNoTracking().Where(d => d.Status == StatusDaDespesa.Prevista).SumAsync(d => (long?)d.ValorEmCentavos, ct) ?? 0;

    /// <inheritdoc />
    public async Task<IReadOnlyList<GastoPorCategoria>> PorCategoria(CancellationToken ct = default)
    {
        var grupos = await db
            .Despesas.AsNoTracking()
            .Where(d => d.Status != StatusDaDespesa.Cancelada)
            .GroupBy(d => d.Categoria)
            .Select(grupo => new GastoPorCategoria(
                grupo.Key,
                grupo.Count(),
                grupo.Sum(d => d.Status == StatusDaDespesa.Paga ? d.ValorEmCentavos : 0),
                grupo.Sum(d => d.Status == StatusDaDespesa.Prevista ? d.ValorEmCentavos : 0)
            ))
            .ToListAsync(ct);

        return [.. grupos.OrderByDescending(g => g.PagoEmCentavos + g.PrevistoEmCentavos).ThenBy(g => g.Categoria)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// As duas pontas separadas e misturadas em memória: são duas tabelas diferentes, e <c>UNION</c> de
    /// projeções no EF Core custa mais para ler do que estas duas consultas de oito linhas.
    /// <para>
    /// A entrada não traz o nome de quem pagou. O caixa é lido por toda a turma — inclusive pelo
    /// formando, a quem esta tela presta contas — e uma lista de quem pagou quando é dado de pessoa,
    /// não de caixa. Quem precisa do nome tem a Conferência e a lista de parcelas, ambas da gestão.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<LancamentoDoCaixa>> UltimosLancamentos(int quantidade, CancellationToken ct = default)
    {
        var entradas = await (
            from recebimento in db.Recebimentos.AsNoTracking()
            where recebimento.EstornadoEm == null
            orderby recebimento.PagoEm descending, recebimento.BaixadoEm descending
            select new LancamentoDoCaixa(recebimento.PagoEm, EntradaDeParcela, recebimento.ValorEmCentavos, true)
        )
            .Take(quantidade)
            .ToListAsync(ct);

        var saidas = await db
            .Despesas.AsNoTracking()
            .Where(d => d.Status == StatusDaDespesa.Paga && d.PagoEm != null)
            .OrderByDescending(d => d.PagoEm)
            .ThenByDescending(d => d.AtualizadoEm)
            .Take(quantidade)
            .Select(d => new LancamentoDoCaixa(d.PagoEm!.Value, d.Descricao, d.ValorEmCentavos, false))
            .ToListAsync(ct);

        return [.. entradas.Concat(saidas).OrderByDescending(lancamento => lancamento.Data).Take(quantidade)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SomaDoMes>> EntradasPorMes(CancellationToken ct = default) =>
        PorMes(
            await db
                .Recebimentos.AsNoTracking()
                .Where(r => r.EstornadoEm == null)
                .GroupBy(r => new { r.PagoEm.Year, r.PagoEm.Month })
                .Select(grupo => new GrupoDoMes(grupo.Key.Year, grupo.Key.Month, grupo.Sum(r => r.ValorEmCentavos)))
                .ToListAsync(ct)
        );

    /// <inheritdoc />
    public async Task<IReadOnlyList<SomaDoMes>> SaidasPorMes(CancellationToken ct = default) =>
        PorMes(
            await db
                .Despesas.AsNoTracking()
                .Where(d => d.Status == StatusDaDespesa.Paga && d.PagoEm != null)
                .GroupBy(d => new { d.PagoEm!.Value.Year, d.PagoEm!.Value.Month })
                .Select(grupo => new GrupoDoMes(grupo.Key.Year, grupo.Key.Month, grupo.Sum(d => d.ValorEmCentavos)))
                .ToListAsync(ct)
        );

    /// <inheritdoc />
    /// <remarks>Só o que ainda vence: parcela vencida não entra em mês nenhum da projeção (decisão 6).</remarks>
    public async Task<IReadOnlyList<SomaDoMes>> EntradasPrevistasPorMes(DateOnly hoje, CancellationToken ct = default) =>
        PorMes(
            await db
                .Parcelas.AsNoTracking()
                .Where(p => p.Status == StatusDaParcela.Aberta && p.Vencimento >= hoje)
                .GroupBy(p => new { p.Vencimento.Year, p.Vencimento.Month })
                .Select(grupo => new GrupoDoMes(grupo.Key.Year, grupo.Key.Month, grupo.Sum(p => p.ValorOriginalEmCentavos)))
                .ToListAsync(ct)
        );

    /// <inheritdoc />
    /// <remarks>A despesa prevista atrasada cai no mês de hoje: o dinheiro ainda vai sair, e ignorá-la deixaria a projeção otimista.</remarks>
    public async Task<IReadOnlyList<SomaDoMes>> SaidasPrevistasPorMes(DateOnly hoje, CancellationToken ct = default)
    {
        var meses = PorMes(
            await db
                .Despesas.AsNoTracking()
                .Where(d => d.Status == StatusDaDespesa.Prevista)
                .GroupBy(d => new { d.Vencimento.Year, d.Vencimento.Month })
                .Select(grupo => new GrupoDoMes(grupo.Key.Year, grupo.Key.Month, grupo.Sum(d => d.ValorEmCentavos)))
                .ToListAsync(ct)
        );

        var mesAtual = new DateOnly(hoje.Year, hoje.Month, 1);

        return
        [
            .. meses
                .GroupBy(soma => soma.Mes < mesAtual ? mesAtual : soma.Mes)
                .Select(grupo => new SomaDoMes(grupo.Key, grupo.Sum(soma => soma.ValorEmCentavos))),
        ];
    }

    /// <summary>Ano e mês viram o primeiro dia do mês aqui, e não na consulta: <c>make_date</c> no <c>GROUP BY</c> descarta o índice.</summary>
    /// <param name="grupos">Grupos como saíram do banco.</param>
    private static IReadOnlyList<SomaDoMes> PorMes(IEnumerable<GrupoDoMes> grupos) =>
        [.. grupos.Select(grupo => new SomaDoMes(new DateOnly(grupo.Ano, grupo.Mes, 1), grupo.ValorEmCentavos))];

    /// <summary>Um mês agrupado, como sai do banco: ano e mês separados.</summary>
    /// <param name="Ano">Ano.</param>
    /// <param name="Mes">Mês, de 1 a 12.</param>
    /// <param name="ValorEmCentavos">Soma do mês.</param>
    private sealed record GrupoDoMes(int Ano, int Mes, long ValorEmCentavos);
}
