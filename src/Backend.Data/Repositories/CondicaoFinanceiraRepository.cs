using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>As condições financeiras da turma (Sprint 54).</summary>
/// <param name="db">Contexto de dados.</param>
public sealed class CondicaoFinanceiraRepository(AppDbContext db) : ICondicaoFinanceiraRepository
{
    /// <inheritdoc />
    public Task<CondicaoFinanceira?> ObterDoVinculo(Guid vinculoId, CancellationToken ct = default) =>
        db.CondicoesFinanceiras.AsNoTracking().FirstOrDefaultAsync(c => c.VinculoId == vinculoId, ct);

    /// <inheritdoc />
    public Task<CondicaoFinanceira?> ObterDoVinculoParaEdicao(Guid vinculoId, CancellationToken ct = default) =>
        db.CondicoesFinanceiras.FirstOrDefaultAsync(c => c.VinculoId == vinculoId, ct);

    /// <inheritdoc />
    public Task<CondicaoFinanceira?> ObterDoConvidadoParaEdicao(string email, CancellationToken ct = default) =>
        db.CondicoesFinanceiras.FirstOrDefaultAsync(c => c.Email == email, ct);

    /// <inheritdoc />
    /// <remarks>Sem filtro global: o aceite do convite roda antes de a sessão ter a turma.</remarks>
    public Task<CondicaoFinanceira?> ObterDoConvidadoDeTodasAsFormaturas(Guid formaturaId, string email, CancellationToken ct = default) =>
        db.CondicoesFinanceiras.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.FormaturaId == formaturaId && c.Email == email, ct);

    /// <inheritdoc />
    public async Task Adicionar(CondicaoFinanceira condicao, CancellationToken ct = default) => await db.CondicoesFinanceiras.AddAsync(condicao, ct);

    /// <inheritdoc />
    public void Remover(CondicaoFinanceira condicao) => db.CondicoesFinanceiras.Remove(condicao);
}
