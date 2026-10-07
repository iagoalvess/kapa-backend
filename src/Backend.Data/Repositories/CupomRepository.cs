using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <inheritdoc />
public sealed class CupomRepository(AppDbContext db) : ICupomRepository
{
    /// <inheritdoc />
    public Task<Cupom?> ObterPorCodigo(string codigo, CancellationToken ct = default) =>
        db.Cupons.AsNoTracking().FirstOrDefaultAsync(c => c.Codigo == codigo, ct);

    /// <inheritdoc />
    public Task<Cupom?> Obter(Guid cupomId, CancellationToken ct = default) => db.Cupons.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cupomId, ct);

    /// <inheritdoc />
    public Task<Cupom?> ObterParaEdicao(Guid cupomId, CancellationToken ct = default) => db.Cupons.FirstOrDefaultAsync(c => c.Id == cupomId, ct);

    /// <inheritdoc />
    /// <remarks>O <c>WHERE</c> repete, em SQL, o <see cref="Cupom.Disponivel"/> — é a conferência feita sob a trava da linha.</remarks>
    public async Task<bool> ReservarUso(Guid cupomId, DateTime agoraUtc, CancellationToken ct = default) =>
        await db
            .Cupons.Where(c => c.Id == cupomId && c.Ativo && c.Usos < c.LimiteDeUsos && c.ValidoAte > agoraUtc)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Usos, c => c.Usos + 1).SetProperty(c => c.AtualizadoEm, agoraUtc), ct) == 1;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Cupom>> Listar(CancellationToken ct = default) =>
        await db.Cupons.AsNoTracking().OrderByDescending(c => c.CriadoEm).ThenByDescending(c => c.Id).ToListAsync(ct);

    /// <inheritdoc />
    public async Task Adicionar(Cupom cupom, CancellationToken ct = default) => await db.Cupons.AddAsync(cupom, ct);
}
