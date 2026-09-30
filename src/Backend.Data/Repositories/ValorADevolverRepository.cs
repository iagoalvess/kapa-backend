using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// A lista "a devolver" da formatura selecionada (Sprint 42).
/// </summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class ValorADevolverRepository(AppDbContext db) : IValorADevolverRepository
{
    /// <inheritdoc />
    /// <remarks>SQL à mão porque o EF Core não expressa <c>FOR UPDATE</c>; o filtro global da formatura vale por fora.</remarks>
    public async Task<ValorADevolver?> Travar(Guid id, CancellationToken ct = default) =>
        await db.ValoresADevolver.FromSql($"SELECT * FROM valores_a_devolver WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValorADevolver>> ListarAbertosDaParcelaParaEdicao(Guid parcelaId, CancellationToken ct = default) =>
        await db
            .ValoresADevolver.Where(v => v.ParcelaId == parcelaId && v.Status == StatusDoValorADevolver.ADevolver)
            .OrderBy(v => v.CriadoEm)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValorADevolver>> ListarAbertosDaCobrancaParaEdicao(Guid cobrancaId, CancellationToken ct = default) =>
        await db.ValoresADevolver.Where(v => v.CobrancaId == cobrancaId && v.Status == StatusDoValorADevolver.ADevolver).ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Os que esperam, dos mais antigos — é a ordem da fila; os resolvidos, dos mais recentes. A busca procura no nome
    /// civil e no da conta, como a lista de parcelas.
    /// </remarks>
    public async Task<PaginaDe<ValorADevolverNaLista>> Listar(
        PaginacaoRequest paginacao,
        FiltroDeValoresADevolver filtro,
        CancellationToken ct = default
    )
    {
        var consulta = filtro.Resolvidos
            ? Linhas().Where(linha => linha.Valor.Status != StatusDoValorADevolver.ADevolver)
            : Linhas().Where(linha => linha.Valor.Status == StatusDoValorADevolver.ADevolver);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = Busca.Padrao(filtro.Busca);
            consulta = consulta.Where(linha =>
                EF.Functions.ILike(EF.Functions.Unaccent(linha.Nome), termo) || EF.Functions.ILike(EF.Functions.Unaccent(linha.NomeDaConta), termo)
            );
        }

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<ValorADevolverNaLista>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "valor" => consulta.Por(x => x.Valor.ValorEmCentavos, desc),
            "formando" => consulta.Por(x => x.Nome, desc),
            "criado" => consulta.Por(x => x.Valor.CriadoEm, desc),
            _ when filtro.Resolvidos => consulta.OrderByDescending(x => x.Valor.ResolvidoEm),
            _ => consulta.OrderBy(x => x.Valor.CriadoEm),
        };

        var itens = await Projetar(ordenada.ThenBy(x => x.Valor.Id).Skip(paginacao.Pular).Take(paginacao.Tamanho)).ToListAsync(ct);

        return new PaginaDe<ValorADevolverNaLista>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    public Task<ValorADevolverNaLista?> Obter(Guid id, CancellationToken ct = default) =>
        Projetar(Linhas().Where(linha => linha.Valor.Id == id)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task Adicionar(ValorADevolver valor, CancellationToken ct = default) => await db.ValoresADevolver.AddAsync(valor, ct);

    /// <summary>
    /// O valor com o formando, o item e a parcela. O nome é o civil do cadastro quando houver, senão o da conta — a
    /// mesma regra das parcelas.
    /// </summary>
    private IQueryable<LinhaDoValorADevolver> Linhas() =>
        from valor in db.ValoresADevolver.AsNoTracking()
        join vinculo in db.Vinculos.AsNoTracking() on valor.VinculoId equals vinculo.Id
        join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
        join item in db.ItensDeCobranca.AsNoTracking() on valor.ItemDeCobrancaId equals item.Id
        join perfil in db.PerfisDeFormandos.AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
        from perfil in perfis.DefaultIfEmpty()
        join parcela in db.Parcelas.AsNoTracking() on valor.ParcelaId equals (Guid?)parcela.Id into parcelas
        from parcela in parcelas.DefaultIfEmpty()
        select new LinhaDoValorADevolver
        {
            Valor = valor,
            UsuarioId = usuario.Id,
            Nome = perfil != null && perfil.NomeCompleto != null ? perfil.NomeCompleto : usuario.Nome,
            NomeDaConta = usuario.Nome,
            Tipo = item.Tipo,
            Descricao = item.Descricao,
            NumeroDaParcela = parcela != null ? parcela.Numero : null,
            Vencimento = parcela != null ? parcela.Vencimento : null,
        };

    private static IQueryable<ValorADevolverNaLista> Projetar(IQueryable<LinhaDoValorADevolver> linhas) =>
        linhas.Select(linha => new ValorADevolverNaLista(
            linha.Valor.Id,
            linha.UsuarioId,
            linha.Nome,
            linha.Valor.Origem,
            linha.Valor.Status,
            linha.Valor.ValorEmCentavos,
            linha.Tipo,
            linha.Descricao,
            linha.NumeroDaParcela,
            linha.Vencimento,
            linha.Valor.CriadoEm,
            linha.Valor.ResolvidoEm,
            linha.Valor.Observacao,
            linha.Valor.ComprovanteArquivoId != null
        ));
}

/// <summary>Um valor a devolver com o formando, o item e a parcela — a forma intermediária da lista.</summary>
/// <remarks>Classe, e não tipo anônimo: passa entre métodos.</remarks>
internal sealed class LinhaDoValorADevolver
{
    /// <summary>O valor.</summary>
    public required ValorADevolver Valor { get; init; }

    /// <summary>Formando.</summary>
    public Guid UsuarioId { get; init; }

    /// <summary>Nome civil, ou o da conta.</summary>
    public required string Nome { get; init; }

    /// <summary>Nome da conta — a busca procura nos dois.</summary>
    public required string NomeDaConta { get; init; }

    /// <summary>Tipo do item de origem.</summary>
    public TipoDeCobranca Tipo { get; init; }

    /// <summary>Descrição do item.</summary>
    public string? Descricao { get; init; }

    /// <summary>Número da parcela, quando há parcela.</summary>
    public int? NumeroDaParcela { get; init; }

    /// <summary>Vencimento dela.</summary>
    public DateOnly? Vencimento { get; init; }
}
