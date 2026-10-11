using Backend.Business.Enquetes.Interfaces;
using Backend.Business.Enquetes.Models;
using Backend.Business.Formaturas.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>Consultas em lote e trava de linha para a turma selecionada.</summary>
public sealed class EnqueteRepository(AppDbContext db) : IEnqueteRepository
{
    /// <inheritdoc />
    public Task<MembroParaEnquete?> ObterMembro(Guid usuarioId, CancellationToken ct = default) =>
        (
            from v in db.Vinculos.AsNoTracking()
            join u in db.Users.AsNoTracking() on v.UsuarioId equals u.Id
            where v.FormaturaId == db.FormaturaAtualId && v.Ativo && u.Ativo && v.UsuarioId == usuarioId
            select new MembroParaEnquete(v.Id, u.EmailConfirmed, db.Adesoes.Any(a => a.VinculoId == v.Id), v.Papel != PapelNaFormatura.Formando)
        ).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<int> ContarAptos(CancellationToken ct = default) =>
        (
            from v in db.Vinculos.AsNoTracking()
            join u in db.Users.AsNoTracking() on v.UsuarioId equals u.Id
            where v.FormaturaId == db.FormaturaAtualId && v.Ativo && u.Ativo && u.EmailConfirmed && db.Adesoes.Any(a => a.VinculoId == v.Id)
            select v.Id
        ).CountAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<EnqueteComContagem>> Listar(Guid vinculoId, CancellationToken ct = default)
    {
        var lista = await db.Enquetes.AsNoTracking().OrderBy(e => e.EncerraEm).ToListAsync(ct);
        var totais = await db
            .RespostasDeEnquetes.AsNoTracking()
            .GroupBy(r => r.EnqueteId)
            .Select(g => new { Id = g.Key, Total = g.Count() })
            .ToDictionaryAsync(x => x.Id, x => x.Total, ct);
        var minhas = await db
            .RespostasDeEnquetes.AsNoTracking()
            .Where(r => r.VinculoId == vinculoId)
            .Include(r => r.Escolhas)
            .ToDictionaryAsync(r => r.EnqueteId, ct);
        return lista
            .Select(e => new EnqueteComContagem(
                e,
                totais.GetValueOrDefault(e.Id),
                minhas.TryGetValue(e.Id, out var minha) ? minha.Escolhas.Select(x => x.OpcaoId).ToList() : null
            ))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<EnqueteComContagem?> Obter(Guid id, Guid vinculoId, CancellationToken ct = default)
    {
        var enquete = await db.Enquetes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (enquete is null)
            return null;
        var total = await db.RespostasDeEnquetes.CountAsync(r => r.EnqueteId == id, ct);
        var minha = await db
            .RespostasDeEnquetes.AsNoTracking()
            .Include(r => r.Escolhas)
            .FirstOrDefaultAsync(r => r.EnqueteId == id && r.VinculoId == vinculoId, ct);
        return new EnqueteComContagem(enquete, total, minha?.Escolhas.Select(x => x.OpcaoId).ToList());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpcaoComResultado>> Resultados(Guid id, int respondentes, CancellationToken ct = default)
    {
        var opcoes = await db
            .OpcoesDeEnquetes.AsNoTracking()
            .Where(o => o.EnqueteId == id)
            .OrderBy(o => o.Ordem)
            .Select(o => new
            {
                o.Id,
                o.Texto,
                o.Ordem,
                Total = db.EscolhasDeEnquetes.Count(e => e.OpcaoId == o.Id),
            })
            .ToListAsync(ct);
        return opcoes
            .Select(o => new OpcaoComResultado(o.Id, o.Texto, o.Ordem, o.Total, respondentes == 0 ? 0 : Math.Round(o.Total * 100m / respondentes, 1)))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParticipanteDaEnquete>> Participantes(Guid id, CancellationToken ct = default) =>
        await (
            from r in db.RespostasDeEnquetes.AsNoTracking()
            join v in db.Vinculos.AsNoTracking() on r.VinculoId equals v.Id
            join u in db.Users.AsNoTracking() on v.UsuarioId equals u.Id
            where r.EnqueteId == id && u.AnonimizadoEm == null
            orderby u.Nome
            select new ParticipanteDaEnquete(u.Nome)
        ).ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>FOR UPDATE atua na transação do service, antes de carregar filhos ou conferir o prazo.</remarks>
    public async Task<Enquete?> Travar(Guid id, CancellationToken ct = default)
    {
        var enquete = await db.Enquetes.FromSql($"SELECT * FROM enquetes WHERE id = {id} FOR UPDATE").FirstOrDefaultAsync(ct);
        if (enquete is not null)
            await db.Entry(enquete).Collection(e => e.Opcoes).LoadAsync(ct);
        return enquete;
    }

    /// <inheritdoc />
    public Task<RespostaDaEnquete?> ObterResposta(Guid id, Guid vinculoId, CancellationToken ct = default) =>
        db.RespostasDeEnquetes.Include(r => r.Escolhas).FirstOrDefaultAsync(r => r.EnqueteId == id && r.VinculoId == vinculoId, ct);

    /// <inheritdoc />
    public async Task Adicionar(Enquete enquete, CancellationToken ct = default) => await db.Enquetes.AddAsync(enquete, ct);

    /// <inheritdoc />
    public void RemoverOpcoes(IReadOnlyList<OpcaoDaEnquete> opcoes) => db.OpcoesDeEnquetes.RemoveRange(opcoes);

    /// <inheritdoc />
    public void AdicionarOpcoes(IReadOnlyList<OpcaoDaEnquete> opcoes) => db.OpcoesDeEnquetes.AddRange(opcoes);

    /// <inheritdoc />
    public async Task AdicionarResposta(RespostaDaEnquete resposta, CancellationToken ct = default) =>
        await db.RespostasDeEnquetes.AddAsync(resposta, ct);

    /// <inheritdoc />
    public void RemoverEscolhas(IReadOnlyList<EscolhaDaResposta> escolhas) => db.EscolhasDeEnquetes.RemoveRange(escolhas);

    /// <inheritdoc />
    public void AdicionarEscolhas(IReadOnlyList<EscolhaDaResposta> escolhas) => db.EscolhasDeEnquetes.AddRange(escolhas);

    /// <inheritdoc />
    public void RemoverResposta(RespostaDaEnquete resposta)
    {
        RemoverEscolhas(resposta.Escolhas);
        db.RespostasDeEnquetes.Remove(resposta);
    }
}
