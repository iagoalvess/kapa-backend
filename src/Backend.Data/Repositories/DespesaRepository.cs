using Backend.Business.Abstractions;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Despesas da formatura selecionada.
/// </summary>
/// <remarks>
/// Despesa e fornecedor são isolados pelo filtro global; o fornecedor entra por <c>LEFT JOIN</c>,
/// porque despesa sem fornecedor é comum (taxa bancária, reembolso).
/// <para>
/// "Atrasada" não é coluna: é prevista com vencimento no passado, resolvida na projeção com o dia de
/// hoje — a mesma regra de <c>Vencida</c> na parcela.
/// </para>
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class DespesaRepository(AppDbContext db) : IDespesaRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Por vencimento, como a tela Parcelas: a próxima a pagar em cima. Ou pela coluna que a tela
    /// pediu; o id continua de desempate — e é ele que põe as parcelas de um lançamento na ordem em
    /// que vencem. "Situação" não ordena: "Atrasada" não é coluna, sai da comparação do vencimento
    /// com hoje, e ordenar pelo <c>Status</c> gravado misturaria atrasada com prevista.
    /// </remarks>
    public async Task<PaginaDe<DespesaResumo>> Listar(
        PaginacaoRequest paginacao,
        FiltroDeDespesas filtro,
        DateOnly hoje,
        CancellationToken ct = default
    )
    {
        var consulta = Filtrar(filtro, hoje);

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<DespesaResumo>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "despesa" => consulta.Por(linha => linha.Despesa.Descricao, desc),
            "vencimento" => consulta.Por(linha => linha.Despesa.Vencimento, desc),
            "valor" => consulta.Por(linha => linha.Despesa.ValorEmCentavos, desc),
            _ => consulta.OrderBy(linha => linha.Despesa.Vencimento),
        };

        var itens = await Projetar(ordenada.ThenBy(linha => linha.Despesa.Id).Skip(paginacao.Pular).Take(paginacao.Tamanho), hoje).ToListAsync(ct);

        return new PaginaDe<DespesaResumo>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContagemDeDespesas>> Contar(FiltroDeDespesas filtro, DateOnly hoje, CancellationToken ct = default) =>
        await Filtrar(filtro with { Status = null, Atrasadas = false }, hoje)
            .GroupBy(linha => new { linha.Despesa.Status, Atrasada = linha.Despesa.Vencimento < hoje })
            .Select(grupo => new ContagemDeDespesas(
                grupo.Key.Status,
                grupo.Key.Atrasada,
                grupo.Count(),
                grupo.Sum(linha => linha.Despesa.ValorEmCentavos)
            ))
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<DespesaResumo?> Obter(Guid id, DateOnly hoje, CancellationToken ct = default) =>
        Projetar(Linhas().Where(linha => linha.Despesa.Id == id), hoje).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<Despesa?> ObterParaEdicao(Guid id, CancellationToken ct = default) => db.Despesas.FirstOrDefaultAsync(d => d.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> ExisteIgual(Guid? fornecedorId, string descricao, DateOnly vencimento, CancellationToken ct = default) =>
        db.Despesas.AnyAsync(
            d => d.FornecedorId == fornecedorId && d.Descricao == descricao && d.Vencimento == vencimento && d.Status != StatusDaDespesa.Cancelada,
            ct
        );

    /// <inheritdoc />
    /// <remarks>Cancelada conta: ela também aponta para o fornecedor, e apagar o cadastro deixaria a linha órfã.</remarks>
    public Task<bool> ExisteDoFornecedor(Guid fornecedorId, CancellationToken ct = default) =>
        db.Despesas.AnyAsync(d => d.FornecedorId == fornecedorId, ct);

    /// <inheritdoc />
    public Task<bool> ExisteDoItemDaFesta(Guid itemDaFestaId, CancellationToken ct = default) =>
        db.Despesas.AnyAsync(d => d.ItemDaFestaId == itemDaFestaId, ct);

    /// <inheritdoc />
    public Task<ComprovanteDaDespesa?> ObterComprovante(Guid id, CancellationToken ct = default) =>
        (
            from despesa in db.Despesas.AsNoTracking()
            join arquivo in db.Arquivos.AsNoTracking() on despesa.ComprovanteArquivoId equals arquivo.Id
            where despesa.Id == id
            select new ComprovanteDaDespesa(arquivo.Id, arquivo.EnviadoPorId)
        ).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task Adicionar(IReadOnlyList<Despesa> despesas, CancellationToken ct = default) => db.Despesas.AddRangeAsync(despesas, ct);

    /// <summary>A despesa com o fornecedor dela — a base de toda leitura daqui.</summary>
    private IQueryable<LinhaDeDespesa> Linhas() =>
        from despesa in db.Despesas.AsNoTracking()
        join fornecedor in db.Fornecedores.AsNoTracking() on despesa.FornecedorId equals fornecedor.Id into fornecedores
        from fornecedor in fornecedores.DefaultIfEmpty()
        select new LinhaDeDespesa { Despesa = despesa, Fornecedor = fornecedor };

    private IQueryable<LinhaDeDespesa> Filtrar(FiltroDeDespesas filtro, DateOnly hoje)
    {
        var consulta = Linhas();

        if (filtro.LancamentoId is { } lancamentoId)
            consulta = consulta.Where(linha => linha.Despesa.LancamentoId == lancamentoId);

        if (filtro.FornecedorId is { } fornecedorId)
            consulta = consulta.Where(linha => linha.Despesa.FornecedorId == fornecedorId);

        if (filtro.Categoria is { } categoria)
            consulta = consulta.Where(linha => linha.Despesa.Categoria == categoria);

        if (filtro.Status is { } status)
            consulta = consulta.Where(linha => linha.Despesa.Status == status);

        if (filtro.Atrasadas)
            consulta = consulta.Where(linha => linha.Despesa.Status == StatusDaDespesa.Prevista && linha.Despesa.Vencimento < hoje);

        if (filtro.De is { } de)
            consulta = consulta.Where(linha => linha.Despesa.Vencimento >= de);

        if (filtro.Ate is { } ate)
            consulta = consulta.Where(linha => linha.Despesa.Vencimento <= ate);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = Busca.Padrao(filtro.Busca);

            consulta = consulta.Where(linha =>
                EF.Functions.ILike(EF.Functions.Unaccent(linha.Despesa.Descricao), termo)
                || (linha.Fornecedor != null && EF.Functions.ILike(EF.Functions.Unaccent(linha.Fornecedor.Nome), termo))
            );
        }

        return consulta;
    }

    private static IQueryable<DespesaResumo> Projetar(IQueryable<LinhaDeDespesa> consulta, DateOnly hoje) =>
        consulta.Select(linha => new DespesaResumo(
            linha.Despesa.Id,
            linha.Despesa.LancamentoId,
            linha.Despesa.FornecedorId,
            linha.Despesa.ItemDaFestaId,
            linha.Fornecedor == null ? null : linha.Fornecedor.Nome,
            linha.Despesa.Descricao,
            linha.Despesa.Categoria,
            linha.Despesa.ValorEmCentavos,
            linha.Despesa.Competencia,
            linha.Despesa.Vencimento,
            linha.Despesa.Numero,
            linha.Despesa.TotalDeParcelas,
            linha.Despesa.Status,
            linha.Despesa.PagoEm,
            linha.Despesa.ComprovanteArquivoId != null,
            linha.Despesa.Status == StatusDaDespesa.Prevista && linha.Despesa.Vencimento < hoje
        ));

    /// <summary>A despesa e o fornecedor dela, antes da projeção.</summary>
    private sealed class LinhaDeDespesa
    {
        /// <summary>A despesa.</summary>
        public required Despesa Despesa { get; init; }

        /// <summary>O fornecedor, quando há.</summary>
        public Fornecedor? Fornecedor { get; init; }
    }
}
