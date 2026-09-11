using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Acesso aos metadados dos arquivos.
/// </summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class ArquivoRepository(AppDbContext db) : IArquivoRepository
{
    /// <inheritdoc />
    public async Task Adicionar(Arquivo arquivo, CancellationToken ct = default) => await db.Arquivos.AddAsync(arquivo, ct);

    /// <inheritdoc />
    public Task<Arquivo?> ObterPorId(Guid id, CancellationToken ct = default) => db.Arquivos.FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <inheritdoc />
    public void Remover(Arquivo arquivo) => db.Arquivos.Remove(arquivo);

    /// <inheritdoc />
    /// <remarks>
    /// Uma única ida ao banco: o <c>GroupBy</c> constante vira um <c>SELECT count(*), sum(tamanho)</c>
    /// sem <c>GROUP BY</c>. <c>Sum</c> sobre conjunto vazio devolve <c>NULL</c> no Postgres, daí o
    /// <c>long?</c> e o <c>?? 0</c> — sem isso, o primeiro envio de cada usuário quebraria.
    /// </remarks>
    public async Task<UsoDeArmazenamento> ObterUsoDoUsuario(Guid enviadoPorId, CancellationToken ct = default)
    {
        var uso = await db
            .Arquivos.AsNoTracking()
            .Where(a => a.EnviadoPorId == enviadoPorId)
            .GroupBy(_ => 1)
            .Select(grupo => new { Quantidade = grupo.Count(), Bytes = (long?)grupo.Sum(a => a.Tamanho) })
            .FirstOrDefaultAsync(ct);

        return uso is null ? new UsoDeArmazenamento(0, 0) : new UsoDeArmazenamento(uso.Quantidade, uso.Bytes ?? 0);
    }

    /// <inheritdoc />
    public async Task<PaginaDe<ArquivoResumo>> Listar(
        PaginacaoRequest paginacao,
        string? categoria,
        Guid? enviadoPorId,
        CancellationToken ct = default
    )
    {
        var consulta = db.Arquivos.AsNoTracking();

        if (enviadoPorId is not null)
            consulta = consulta.Where(a => a.EnviadoPorId == enviadoPorId);

        if (!string.IsNullOrWhiteSpace(categoria))
            consulta = consulta.Where(a => a.Categoria == categoria);

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<ArquivoResumo>.Vazia(paginacao);

        var itens = await consulta
            .OrderByDescending(a => a.CriadoEm)
            .ThenBy(a => a.Id)
            .Skip(paginacao.Pular)
            .Take(paginacao.Tamanho)
            .Select(a => new ArquivoResumo(a.Id, a.Nome, a.ContentType, a.Tamanho, a.Categoria, a.EnviadoPorId, a.CriadoEm))
            .ToListAsync(ct);

        return new PaginaDe<ArquivoResumo>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }
}
