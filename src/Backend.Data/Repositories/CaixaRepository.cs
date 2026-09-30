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
/// O único lugar do projeto que lê quatro agregados na mesma pergunta — recebimento, parcela, despesa
/// e receita. Cada método é uma consulta agregada no banco: nada aqui traz lançamento para a memória
/// para somar em C#, que é como um caixa de 2.000 linhas passa de milissegundos a segundos.
/// <para>
/// Entrada tem duas origens desde a Sprint 28 — a parcela paga e a receita recebida —, e onde as
/// duas se somam elas vão juntas na mesma consulta, por <see cref="EntradasDeDinheiro"/> (um
/// <c>UNION ALL</c>): duas idas ao banco somadas em memória é como o extrato de um mês vira N+1.
/// </para>
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
        await EntradasDeDinheiro.Realizadas(db).SumAsync(e => (long?)e.Valor, ct) ?? 0;

    /// <inheritdoc />
    public async Task<long> Gasto(CancellationToken ct = default) =>
        await db.Despesas.AsNoTracking().Where(d => d.Status == StatusDaDespesa.Paga).SumAsync(d => (long?)d.ValorEmCentavos, ct) ?? 0;

    /// <inheritdoc />
    /// <remarks>
    /// Pelo valor original, menos o que já entrou em pagamento parcial (esse já está no arrecadado), sem
    /// multa nem juros: o que cada formando vai pagar a mais depende do dia em
    /// que ele pagar, e projeção não adivinha atraso. Uma consulta, dois grupos.
    /// </remarks>
    public async Task<(long AVencer, long EmAtraso)> ParcelasEmAberto(DateOnly hoje, CancellationToken ct = default)
    {
        var grupos = await db
            .Parcelas.AsNoTracking()
            .Where(p => p.Status == StatusDaParcela.Aberta)
            .GroupBy(p => p.Vencimento < hoje)
            .Select(grupo => new { Atrasada = grupo.Key, Valor = grupo.Sum(p => p.ValorOriginalEmCentavos - (p.ValorPagoEmCentavos ?? 0)) })
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
    /// <remarks>Receita cancelada fica de fora; a prevista aparece à parte, e nunca soma no arrecadado (P2).</remarks>
    public async Task<IReadOnlyList<OutraReceitaPorCategoria>> OutrasReceitasPorCategoria(CancellationToken ct = default)
    {
        var grupos = await db
            .OutrasReceitas.AsNoTracking()
            .Where(r => r.Status != StatusDaOutraReceita.Cancelada)
            .GroupBy(r => r.Categoria)
            .Select(grupo => new OutraReceitaPorCategoria(
                grupo.Key,
                grupo.Count(),
                grupo.Sum(r => r.Status == StatusDaOutraReceita.Recebida ? r.ValorEmCentavos : 0),
                grupo.Sum(r => r.Status == StatusDaOutraReceita.Prevista ? r.ValorEmCentavos : 0)
            ))
            .ToListAsync(ct);

        return [.. grupos.OrderByDescending(g => g.RecebidoEmCentavos + g.PrevistoEmCentavos).ThenBy(g => g.Categoria)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Entradas numa consulta (parcela e receita, por <c>UNION ALL</c>) e saídas em outra, misturadas
    /// em memória: são oito linhas de cada lado, e o que se junta aqui é a lista, não uma soma.
    /// <para>
    /// A entrada não traz o nome de quem pagou. O caixa é lido por toda a turma — inclusive pelo
    /// formando, a quem esta tela presta contas — e uma lista de quem pagou quando é dado de pessoa,
    /// não de caixa. Quem precisa do nome tem a Conferência e a lista de parcelas, ambas da gestão.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<LancamentoDoCaixa>> UltimosLancamentos(int quantidade, CancellationToken ct = default)
    {
        var entradas = await db
            .Recebimentos.AsNoTracking()
            .Where(r => r.EstornadoEm == null)
            .Select(r => new
            {
                Data = r.PagoEm,
                Descricao = EntradaDeParcela,
                Valor = r.ValorEmCentavos,
                Momento = r.BaixadoEm,
            })
            .Concat(
                db.OutrasReceitas.AsNoTracking()
                    .Where(r => r.Status == StatusDaOutraReceita.Recebida)
                    .Select(r => new
                    {
                        r.Data,
                        r.Descricao,
                        Valor = r.ValorEmCentavos,
                        Momento = r.AtualizadoEm,
                    })
            )
            .OrderByDescending(e => e.Data)
            .ThenByDescending(e => e.Momento)
            .Take(quantidade)
            .Select(e => new LancamentoDoCaixa(e.Data, e.Descricao, e.Valor, true))
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
            await EntradasDeDinheiro
                .Realizadas(db)
                .GroupBy(e => new { e.Data.Year, e.Data.Month })
                .Select(grupo => new GrupoDoMes(grupo.Key.Year, grupo.Key.Month, grupo.Sum(e => e.Valor)))
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
    /// <remarks>
    /// Só o que ainda vence: parcela vencida não entra em mês nenhum da projeção (decisão 6), e receita
    /// prevista atrasada também não — dinheiro prometido que não veio não é conta com que se conte.
    /// </remarks>
    public async Task<IReadOnlyList<SomaDoMes>> EntradasPrevistasPorMes(
        DateOnly hoje,
        bool comOutrasReceitasPrevistas,
        CancellationToken ct = default
    )
    {
        var previstas = db
            .Parcelas.AsNoTracking()
            .Where(p => p.Status == StatusDaParcela.Aberta && p.Vencimento >= hoje)
            .Select(p => new EntradaDeDinheiro { Data = p.Vencimento, Valor = p.ValorOriginalEmCentavos - (p.ValorPagoEmCentavos ?? 0) });

        if (comOutrasReceitasPrevistas)
            previstas = previstas.Concat(
                db.OutrasReceitas.AsNoTracking()
                    .Where(r => r.Status == StatusDaOutraReceita.Prevista && r.Data >= hoje)
                    .Select(r => new EntradaDeDinheiro { Data = r.Data, Valor = r.ValorEmCentavos })
            );

        return PorMes(
            await previstas
                .GroupBy(e => new { e.Data.Year, e.Data.Month })
                .Select(grupo => new GrupoDoMes(grupo.Key.Year, grupo.Key.Month, grupo.Sum(e => e.Valor)))
                .ToListAsync(ct)
        );
    }

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
