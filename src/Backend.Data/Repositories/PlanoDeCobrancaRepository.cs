using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Planos de cobrança da formatura selecionada.
/// </summary>
/// <remarks>Plano e itens são isolados pelo filtro global: nenhuma consulta leva a formatura.</remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class PlanoDeCobrancaRepository(AppDbContext db) : IPlanoDeCobrancaRepository
{
    /// <inheritdoc />
    /// <remarks>Poucas linhas por turma — é configuração, não movimento —, por isso sem paginação.</remarks>
    public async Task<IReadOnlyList<PlanoDeCobrancaResumo>> Listar(CancellationToken ct = default) =>
        await db
            .PlanosDeCobranca.AsNoTracking()
            .OrderByDescending(p => p.Status == StatusDoPlano.Vigente)
            .ThenByDescending(p => p.CriadoEm)
            .ThenBy(p => p.Id)
            .Select(p => new PlanoDeCobrancaResumo(p.Id, p.Nome, p.Status, p.VigenteDesde))
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<PlanoDeCobranca?> Obter(Guid planoId, CancellationToken ct = default) =>
        db.PlanosDeCobranca.AsNoTracking().Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == planoId, ct);

    /// <inheritdoc />
    public Task<PlanoDeCobranca?> ObterParaEdicao(Guid planoId, CancellationToken ct = default) =>
        db.PlanosDeCobranca.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == planoId, ct);

    /// <inheritdoc />
    public Task<PlanoDeCobranca?> ObterVigente(CancellationToken ct = default) =>
        db.PlanosDeCobranca.AsNoTracking().Include(p => p.Itens).FirstOrDefaultAsync(p => p.Status == StatusDoPlano.Vigente, ct);

    /// <inheritdoc />
    public Task<PlanoDeCobranca?> ObterVigenteParaEdicao(CancellationToken ct = default) =>
        db.PlanosDeCobranca.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Status == StatusDoPlano.Vigente, ct);

    /// <inheritdoc />
    public Task<bool> ExisteVigente(CancellationToken ct = default) => db.PlanosDeCobranca.AnyAsync(p => p.Status == StatusDoPlano.Vigente, ct);

    /// <inheritdoc />
    public async Task Adicionar(PlanoDeCobranca plano, CancellationToken ct = default) => await db.PlanosDeCobranca.AddAsync(plano, ct);

    /// <inheritdoc />
    public async Task AdicionarItem(ItemDeCobranca item, CancellationToken ct = default) => await db.ItensDeCobranca.AddAsync(item, ct);

    /// <inheritdoc />
    public void RemoverItem(ItemDeCobranca item) => db.ItensDeCobranca.Remove(item);
}
