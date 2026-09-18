using Backend.Business.Abstractions;
using Backend.Business.Leads.Interfaces;
using Backend.Business.Leads.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Contatos deixados na página institucional.
/// </summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class LeadRepository(AppDbContext db) : ILeadRepository
{
    /// <inheritdoc />
    public async Task Adicionar(Lead lead, CancellationToken ct = default) => await db.Leads.AddAsync(lead, ct);

    /// <inheritdoc />
    public async Task<PaginaDe<LeadResumo>> Listar(PaginacaoRequest paginacao, string? busca, CancellationToken ct = default)
    {
        var consulta = db.Leads.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = Busca.Padrao(busca);

            consulta = consulta.Where(l =>
                EF.Functions.ILike(EF.Functions.Unaccent(l.Nome), termo)
                || EF.Functions.ILike(l.Email, termo)
                || EF.Functions.ILike(EF.Functions.Unaccent(l.Instituicao), termo)
                || EF.Functions.ILike(EF.Functions.Unaccent(l.Curso), termo)
            );
        }

        var total = await consulta.LongCountAsync(ct);

        var itens = await consulta
            .OrderByDescending(l => l.CriadoEm)
            .ThenByDescending(l => l.Id)
            .Skip((paginacao.Pagina - 1) * paginacao.Tamanho)
            .Take(paginacao.Tamanho)
            .Select(l => new LeadResumo(
                l.Id,
                l.Nome,
                l.Email,
                l.Telefone,
                l.Instituicao,
                l.Curso,
                l.TamanhoDaTurma,
                l.PrevisaoDeColacao,
                l.Mensagem,
                l.Origem,
                l.Meio,
                l.Campanha,
                l.CriadoEm
            ))
            .ToListAsync(ct);

        return new PaginaDe<LeadResumo>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    public Task<bool> JaRegistrado(string email, DateTime desde, CancellationToken ct = default) =>
        db.Leads.AsNoTracking().AnyAsync(l => l.Email == email && l.CriadoEm >= desde, ct);
}
