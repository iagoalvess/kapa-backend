using System.Runtime.CompilerServices;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Relatorios.Interfaces;
using Backend.Business.Relatorios.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// As agregações que o caixa não conhece: adimplência, fornecedores, inadimplentes e as exportações.
/// </summary>
/// <remarks>
/// <b>Nada aqui repete o <see cref="CaixaRepository"/></b> (decisão 4 da Sprint 12). Arrecadado,
/// gasto, saldo, o quadro por categoria e o fluxo mês a mês continuam saindo de lá — este repositório
/// responde às perguntas que só o relatório faz.
/// <para>
/// As duas listagens de exportação são <see cref="IAsyncEnumerable{T}"/> de ponta a ponta
/// (<c>AsAsyncEnumerable</c>, sem <c>ToListAsync</c>): a linha vai para a resposta HTTP assim que sai
/// do banco. Cinco mil parcelas nunca existem juntas na memória do processo.
/// </para>
/// <para>
/// As linhas de parcela reusam <c>ParcelaRepository.Linhas</c>: é a mesma junção da tela de Parcelas,
/// com a mesma regra de nome (o civil do cadastro, senão o da conta). Duplicá-la aqui faria o CSV e a
/// tela chamarem a mesma pessoa por nomes diferentes.
/// </para>
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class RelatorioRepository(AppDbContext db) : IRelatorioRepository
{
    /// <summary>O rótulo das saídas sem fornecedor cadastrado — uma taxa bancária, um reembolso.</summary>
    private const string SemFornecedor = "Sem fornecedor";

    /// <inheritdoc />
    /// <remarks>
    /// Denominador é só o que já venceu (decisão 6): a parcela de dezembro não conta como inadimplência
    /// em março. Uma consulta, dois grupos — paga ou não.
    /// </remarks>
    public async Task<Adimplencia> Adimplencia(DateOnly hoje, CancellationToken ct = default)
    {
        var grupos = await db
            .Parcelas.AsNoTracking()
            .Where(p => p.Vencimento <= hoje && (p.Status == StatusDaParcela.Aberta || p.Status == StatusDaParcela.Paga))
            .GroupBy(p => p.Status == StatusDaParcela.Paga)
            .Select(grupo => new { Paga = grupo.Key, Valor = grupo.Sum(p => p.ValorOriginalEmCentavos) })
            .ToListAsync(ct);

        return new Adimplencia(grupos.Sum(grupo => grupo.Valor), grupos.Where(grupo => grupo.Paga).Sum(grupo => grupo.Valor));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Junção à esquerda: a despesa sem fornecedor cadastrado vira uma linha própria, em vez de sumir
    /// do quadro — e um balancete em que a soma das linhas não bate com o total é um balancete que a
    /// assembleia não aceita.
    /// </remarks>
    public async Task<IReadOnlyList<GastoPorFornecedor>> PorFornecedor(PeriodoDoRelatorio? periodo, CancellationToken ct = default)
    {
        var consulta =
            from despesa in db.Despesas.AsNoTracking()
            where despesa.Status != StatusDaDespesa.Cancelada
            join fornecedor in db.Fornecedores.AsNoTracking() on despesa.FornecedorId equals fornecedor.Id into achados
            from fornecedor in achados.DefaultIfEmpty()
            select new
            {
                despesa.FornecedorId,
                Nome = fornecedor != null ? fornecedor.Nome : SemFornecedor,
                despesa.Status,
                despesa.ValorEmCentavos,
                despesa.PagoEm,
            };

        if (periodo is { } janela)
            consulta = consulta.Where(linha => linha.PagoEm != null && linha.PagoEm >= janela.De && linha.PagoEm <= janela.Ate);

        var grupos = await consulta
            .GroupBy(linha => new { linha.FornecedorId, linha.Nome })
            .Select(grupo => new GastoPorFornecedor(
                grupo.Key.FornecedorId,
                grupo.Key.Nome,
                grupo.Count(),
                grupo.Sum(linha => linha.Status == StatusDaDespesa.Paga ? linha.ValorEmCentavos : 0),
                grupo.Sum(linha => linha.Status == StatusDaDespesa.Prevista ? linha.ValorEmCentavos : 0)
            ))
            .ToListAsync(ct);

        return [.. grupos.OrderByDescending(g => g.PagoEmCentavos + g.PrevistoEmCentavos).ThenBy(g => g.Nome, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pelo dia do pagamento, não pelo do vencimento: o balancete responde o que entrou no caixa no
    /// período, e uma parcela de março paga em junho é dinheiro de junho.
    /// </remarks>
    public async Task<IReadOnlyList<LinhaDeBalancete>> EntradasPorTipo(PeriodoDoRelatorio periodo, CancellationToken ct = default)
    {
        var grupos = await (
            from recebimento in db.Recebimentos.AsNoTracking()
            where recebimento.EstornadoEm == null && recebimento.PagoEm >= periodo.De && recebimento.PagoEm <= periodo.Ate
            join parcela in db.Parcelas.AsNoTracking() on recebimento.ParcelaId equals parcela.Id
            join item in db.ItensDeCobranca.AsNoTracking() on parcela.ItemDeCobrancaId equals item.Id
            group recebimento by new { item.Tipo, item.Descricao } into grupo
            select new
            {
                grupo.Key.Tipo,
                grupo.Key.Descricao,
                Quantidade = grupo.Count(),
                Valor = grupo.Sum(r => r.ValorEmCentavos),
            }
        ).ToListAsync(ct);

        return
        [
            .. grupos
                .Select(grupo => new LinhaDeBalancete(RotuloDoItem.De(grupo.Tipo, grupo.Descricao), grupo.Quantidade, grupo.Valor))
                .OrderByDescending(linha => linha.ValorEmCentavos)
                .ThenBy(linha => linha.Rotulo, StringComparer.Ordinal),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LinhaDeBalancete>> SaidasPorCategoria(PeriodoDoRelatorio periodo, CancellationToken ct = default)
    {
        var grupos = await db
            .Despesas.AsNoTracking()
            .Where(d => d.Status == StatusDaDespesa.Paga && d.PagoEm >= periodo.De && d.PagoEm <= periodo.Ate)
            .GroupBy(d => d.Categoria)
            .Select(grupo => new
            {
                Categoria = grupo.Key,
                Quantidade = grupo.Count(),
                Valor = grupo.Sum(d => d.ValorEmCentavos),
            })
            .ToListAsync(ct);

        return
        [
            .. grupos
                .Select(grupo => new LinhaDeBalancete(RotuloDaCategoria.De(grupo.Categoria), grupo.Quantidade, grupo.Valor))
                .OrderByDescending(linha => linha.ValorEmCentavos)
                .ThenBy(linha => linha.Rotulo, StringComparer.Ordinal),
        ];
    }

    /// <inheritdoc />
    public async Task<TotaisDoPeriodo> Totais(PeriodoDoRelatorio periodo, CancellationToken ct = default) =>
        new(
            await db
                .Recebimentos.AsNoTracking()
                .Where(r => r.EstornadoEm == null && r.PagoEm >= periodo.De && r.PagoEm <= periodo.Ate)
                .SumAsync(r => r.ValorEmCentavos, ct),
            await db
                .Despesas.AsNoTracking()
                .Where(d => d.Status == StatusDaDespesa.Paga && d.PagoEm >= periodo.De && d.PagoEm <= periodo.Ate)
                .SumAsync(d => d.ValorEmCentavos, ct)
        );

    /// <inheritdoc />
    /// <remarks>
    /// Não é o <c>EntradasPorMes</c> do <see cref="CaixaRepository"/>, e não repete a decisão 4: lá é
    /// a vida inteira da turma com projeção, para o gráfico do caixa; aqui é só o realizado, recortado
    /// pelos dias do período, para os meses fecharem com o total do balancete.
    /// <para>
    /// Os meses sem movimento entram zerados: buraco no meio da série faria a curva saltar de março
    /// para junho como se fossem dois meses seguidos.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<MesDoBalancete>> MovimentoPorMes(PeriodoDoRelatorio periodo, CancellationToken ct = default)
    {
        var entradas = Indexar(
            await db
                .Recebimentos.AsNoTracking()
                .Where(r => r.EstornadoEm == null && r.PagoEm >= periodo.De && r.PagoEm <= periodo.Ate)
                .GroupBy(r => new { r.PagoEm.Year, r.PagoEm.Month })
                .Select(grupo => new GrupoDoMes(grupo.Key.Year, grupo.Key.Month, grupo.Sum(r => r.ValorEmCentavos)))
                .ToListAsync(ct)
        );

        var saidas = Indexar(
            await db
                .Despesas.AsNoTracking()
                .Where(d => d.Status == StatusDaDespesa.Paga && d.PagoEm >= periodo.De && d.PagoEm <= periodo.Ate)
                .GroupBy(d => new { d.PagoEm!.Value.Year, d.PagoEm!.Value.Month })
                .Select(grupo => new GrupoDoMes(grupo.Key.Year, grupo.Key.Month, grupo.Sum(d => d.ValorEmCentavos)))
                .ToListAsync(ct)
        );

        var meses = new List<MesDoBalancete>();

        for (var mes = new DateOnly(periodo.De.Year, periodo.De.Month, 1); mes <= periodo.Ate; mes = mes.AddMonths(1))
            meses.Add(new MesDoBalancete(mes, entradas.GetValueOrDefault(mes), saidas.GetValueOrDefault(mes)));

        return meses;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pelo vencimento: a exportação de despesas é a conferência das contas do período, pagas ou não.
    /// <para>
    /// Os três recortes entram como <c>where</c> antes da junção do fornecedor — todos são colunas
    /// gravadas na própria despesa, inclusive a situação, que aqui não tem estado derivado.
    /// </para>
    /// </remarks>
    public IAsyncEnumerable<DespesaExportada> ListarDespesas(FiltroDoRelatorio filtro, CancellationToken ct = default)
    {
        var despesas = db.Despesas.AsNoTracking().Where(d => d.Vencimento >= filtro.Periodo.De && d.Vencimento <= filtro.Periodo.Ate);

        if (filtro.FornecedorId is { } fornecedorId)
            despesas = despesas.Where(d => d.FornecedorId == fornecedorId);

        if (filtro.Categoria is { } categoria)
            despesas = despesas.Where(d => d.Categoria == categoria);

        if (filtro.SituacaoDaDespesa is { } situacao)
            despesas = despesas.Where(d => d.Status == situacao);

        return (
            from despesa in despesas
            join fornecedor in db.Fornecedores.AsNoTracking() on despesa.FornecedorId equals fornecedor.Id into achados
            from fornecedor in achados.DefaultIfEmpty()
            orderby despesa.Vencimento, despesa.Descricao
            select new DespesaExportada(
                despesa.Vencimento,
                despesa.Competencia,
                despesa.Descricao,
                despesa.Categoria,
                fornecedor != null ? fornecedor.Nome : string.Empty,
                despesa.Status,
                despesa.ValorEmCentavos,
                despesa.PagoEm
            )
        ).AsAsyncEnumerable();
    }

    /// <inheritdoc />
    /// <remarks>
    /// O nome do formando sai de <c>ParcelaRepository.Devedores</c>, e não de uma junção própria: a
    /// regra de qual nome usar — o civil do cadastro quando houver, senão o da conta — mora lá e só
    /// lá. O preço é que um formando sem parcela nenhuma não tem nome aqui; o relatório dele também
    /// não teria linha.
    /// <para>
    /// <c>RotuloDoItem</c> é um <c>switch</c> em C# e não traduz para SQL: o que vem do banco são o
    /// tipo e a descrição, e o rótulo se monta depois.
    /// </para>
    /// </remarks>
    public async Task<NomesDoFiltro> NomesDoFiltro(FiltroDoRelatorio filtro, CancellationToken ct = default)
    {
        var fornecedor = filtro.FornecedorId is { } fornecedorId
            ? await db.Fornecedores.AsNoTracking().Where(f => f.Id == fornecedorId).Select(f => f.Nome).FirstOrDefaultAsync(ct)
            : null;

        var formando = filtro.FormandoId is { } formandoId
            ? await ParcelaRepository.Devedores(db).Where(d => d.UsuarioId == formandoId).Select(d => d.Nome).FirstOrDefaultAsync(ct)
            : null;

        var item = filtro.ItemDeCobrancaId is { } itemId
            ? await db.ItensDeCobranca.AsNoTracking().Where(i => i.Id == itemId).Select(i => new { i.Tipo, i.Descricao }).FirstOrDefaultAsync(ct)
            : null;

        return new NomesDoFiltro(fornecedor, formando, item is null ? null : RotuloDoItem.De(item.Tipo, item.Descricao));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Os formandos saem de <c>Devedores</c> com <c>Distinct</c>: é quem tem parcela, com o nome da
    /// mesma regra da tela de Parcelas. Filtrar por quem não tem nenhuma devolveria vazio, então a
    /// lista não os oferece.
    /// </remarks>
    public async Task<OpcoesDeFiltro> OpcoesDeFiltro(CancellationToken ct = default)
    {
        var fornecedores = await db.Fornecedores.AsNoTracking().OrderBy(f => f.Nome).Select(f => new OpcaoDeFiltro(f.Id, f.Nome)).ToListAsync(ct);

        // Distinct sobre tipo anônimo, e não sobre o OpcaoDeFiltro: o OrderBy depois de um Distinct
        // de projeção com construtor não traduz — o endpoint respondia 500.
        var formandos = await ParcelaRepository
            .Devedores(db)
            .Select(d => new { d.UsuarioId, d.Nome })
            .Distinct()
            .OrderBy(o => o.Nome)
            .Select(o => new OpcaoDeFiltro(o.UsuarioId, o.Nome))
            .ToListAsync(ct);

        var itens = await db
            .ItensDeCobranca.AsNoTracking()
            .Select(i => new
            {
                i.Id,
                i.Tipo,
                i.Descricao,
            })
            .ToListAsync(ct);

        return new OpcoesDeFiltro(
            fornecedores,
            formandos,
            [.. itens.Select(i => new OpcaoDeFiltro(i.Id, RotuloDoItem.De(i.Tipo, i.Descricao))).OrderBy(o => o.Nome)]
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Sai de <c>ParcelaRepository.Devedores</c>, e não de <c>Linhas</c>: aquela projeção carrega a
    /// subconsulta de "está em conferência", que a tela usa e a planilha não — e é ela que o
    /// <c>ORDER BY</c> desta consulta não consegue traduzir. A regra do nome continua vindo de um
    /// lugar só.
    /// <para>
    /// <c>RotuloDoItem</c> roda em memória, na projeção final: é um <c>switch</c> em C#, e o EF Core
    /// não o traduz para SQL. As linhas continuam chegando uma a uma.
    /// </para>
    /// </remarks>
    public async IAsyncEnumerable<ParcelaExportada> ListarParcelas(FiltroDoRelatorio filtro, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var devedores = ParcelaRepository
            .Devedores(db)
            .Where(d => d.Parcela.Vencimento >= filtro.Periodo.De && d.Parcela.Vencimento <= filtro.Periodo.Ate);

        if (filtro.FormandoId is { } formandoId)
            devedores = devedores.Where(d => d.UsuarioId == formandoId);

        if (filtro.ItemDeCobrancaId is { } itemDeCobrancaId)
            devedores = devedores.Where(d => d.Parcela.ItemDeCobrancaId == itemDeCobrancaId);

        var consulta =
            from devedor in devedores
            join item in db.ItensDeCobranca.AsNoTracking() on devedor.Parcela.ItemDeCobrancaId equals item.Id
            orderby devedor.Parcela.Vencimento, devedor.Nome
            select new
            {
                devedor.Nome,
                item.Tipo,
                item.Descricao,
                item.NumeroDeParcelas,
                devedor.Parcela,
            };

        await foreach (var linha in consulta.AsAsyncEnumerable().WithCancellation(ct))
            yield return new ParcelaExportada(
                linha.Nome,
                RotuloDoItem.De(linha.Tipo, linha.Descricao),
                linha.Parcela.Numero,
                linha.NumeroDeParcelas,
                linha.Parcela.Vencimento,
                linha.Parcela.ValorOriginalEmCentavos,
                linha.Parcela.Status,
                linha.Parcela.ValorPagoEmCentavos,
                linha.Parcela.PagoEm
            );
    }

    /// <summary>Os meses agrupados, indexados pelo primeiro dia de cada um.</summary>
    /// <param name="grupos">Grupos como saem do banco.</param>
    private static Dictionary<DateOnly, long> Indexar(IEnumerable<GrupoDoMes> grupos) =>
        grupos.ToDictionary(grupo => new DateOnly(grupo.Ano, grupo.Mes, 1), grupo => grupo.ValorEmCentavos);

    /// <summary>Um mês agrupado, como sai do banco: ano e mês separados.</summary>
    /// <param name="Ano">Ano.</param>
    /// <param name="Mes">Mês, de 1 a 12.</param>
    /// <param name="ValorEmCentavos">Soma do mês.</param>
    private sealed record GrupoDoMes(int Ano, int Mes, long ValorEmCentavos);
}
