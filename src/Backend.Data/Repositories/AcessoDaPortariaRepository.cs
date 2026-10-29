using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>Os acessos da portaria terceirizada.</summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class AcessoDaPortariaRepository(AppDbContext db) : IAcessoDaPortariaRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AcessoDaPortaria>> Listar(Guid eventoId, CancellationToken ct = default) =>
        await db
            .AcessosDaPortaria.AsNoTracking()
            .Where(a => a.EventoId == eventoId && a.RevogadoEm == null)
            .OrderByDescending(a => a.CriadoEm)
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<AcessoDaPortaria?> ObterParaEdicao(Guid id, CancellationToken ct = default) =>
        db.AcessosDaPortaria.FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <inheritdoc />
    public Task<AcessoDaPortaria?> ObterPeloTokenDeTodasAsFormaturas(string token, CancellationToken ct = default) =>
        db.AcessosDaPortaria.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Token == token && a.RevogadoEm == null && a.AtivadoEm == null, ct);

    /// <inheritdoc />
    /// <remarks>Corre na autorização, antes de a turma da sessão existir para o filtro global.</remarks>
    public Task<bool> AtivoDeTodasAsFormaturas(Guid id, Guid formaturaId, CancellationToken ct = default) =>
        db
            .AcessosDaPortaria.IgnoreQueryFilters()
            .AnyAsync(a => a.Id == id && a.FormaturaId == formaturaId && a.AtivadoEm != null && a.RevogadoEm == null, ct);

    /// <inheritdoc />
    public async Task Adicionar(AcessoDaPortaria acesso, CancellationToken ct = default) => await db.AcessosDaPortaria.AddAsync(acesso, ct);
}
