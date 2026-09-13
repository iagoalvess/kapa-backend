using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Consultas e escrita da formatura em si.
/// </summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class FormaturaRepository(AppDbContext db) : IFormaturaRepository
{
    /// <inheritdoc />
    public Task<FormaturaDetalhe?> ObterDetalhe(Guid formaturaId, CancellationToken ct = default) =>
        db
            .Formaturas.AsNoTracking()
            .Where(f => f.Id == formaturaId)
            .Select(f => new FormaturaDetalhe(
                f.Id,
                f.Nome,
                f.Instituicao,
                f.Curso,
                f.Ano,
                f.Semestre,
                f.PrevisaoDeColacao,
                f.QuantidadeEstimadaDeFormandos,
                f.Status,
                f.CriadoEm,
                f.AtivadaEm,
                f.EncerradaEm
            ))
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<StatusDaFormatura?> ObterStatus(Guid formaturaId, CancellationToken ct = default) =>
        db.Formaturas.AsNoTracking().Where(f => f.Id == formaturaId).Select(f => (StatusDaFormatura?)f.Status).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<Formatura?> ObterParaEdicao(Guid formaturaId, CancellationToken ct = default) =>
        db.Formaturas.FirstOrDefaultAsync(f => f.Id == formaturaId, ct);

    /// <inheritdoc />
    public Task<bool> ExisteRascunhoCriadoPor(Guid usuarioId, CancellationToken ct = default) =>
        db.Formaturas.AnyAsync(f => f.CriadoPorUsuarioId == usuarioId && f.Status == StatusDaFormatura.Rascunho, ct);

    /// <inheritdoc />
    public async Task Adicionar(Formatura formatura, CancellationToken ct = default) => await db.Formaturas.AddAsync(formatura, ct);
}
