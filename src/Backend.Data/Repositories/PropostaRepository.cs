using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// As propostas dos itens da festa.
/// </summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class PropostaRepository(AppDbContext db) : IPropostaRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Da mais barata para a mais cara, com o id de desempate: sem ele, duas propostas do mesmo valor
    /// trocariam de lugar entre duas aberturas da tela.
    /// </remarks>
    public async Task<IReadOnlyList<PropostaResumo>> Listar(Guid itemId, CancellationToken ct = default) =>
        await Projetar(db.PropostasDoItem.AsNoTracking().Where(p => p.ItemDaFestaId == itemId).OrderBy(p => p.ValorEmCentavos).ThenBy(p => p.Id))
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<PropostaResumo?> Obter(Guid id, CancellationToken ct = default) =>
        Projetar(db.PropostasDoItem.AsNoTracking().Where(p => p.Id == id)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<PropostaDoItem?> ObterParaEdicao(Guid id, CancellationToken ct = default) =>
        db.PropostasDoItem.FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <inheritdoc />
    public async Task Adicionar(PropostaDoItem proposta, CancellationToken ct = default) => await db.PropostasDoItem.AddAsync(proposta, ct);

    /// <inheritdoc />
    public void Remover(PropostaDoItem proposta) => db.PropostasDoItem.Remove(proposta);

    private static IQueryable<PropostaResumo> Projetar(IQueryable<PropostaDoItem> consulta) =>
        consulta.Select(p => new PropostaResumo(p.Id, p.Titulo, p.ValorEmCentavos, p.OQueInclui));
}
