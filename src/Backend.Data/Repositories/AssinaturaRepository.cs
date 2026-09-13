using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Planos, assinaturas e eventos de cobrança.
/// </summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class AssinaturaRepository(AppDbContext db) : IAssinaturaRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PlanoResumo>> ListarPlanosAtivos(CancellationToken ct = default) =>
        await db
            .Planos.AsNoTracking()
            .Where(p => p.Ativo)
            .OrderBy(p => p.PrecoEmCentavos)
            .ThenBy(p => p.Id)
            .Select(p => new PlanoResumo(p.Id, p.Codigo, p.Nome, p.PrecoEmCentavos, p.Ciclo, p.LimiteDeFormandos, p.Recomendado))
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<Plano?> ObterPlanoAtivo(string codigo, CancellationToken ct = default) =>
        db.Planos.AsNoTracking().FirstOrDefaultAsync(p => p.Codigo == codigo && p.Ativo, ct);

    /// <inheritdoc />
    public Task<Plano?> ObterPlano(Guid planoId, CancellationToken ct = default) =>
        db.Planos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planoId, ct);

    /// <inheritdoc />
    public Task<AssinaturaDetalhe?> ObterDetalheDaMaisRecente(CancellationToken ct = default) =>
        (
            from assinatura in db.Assinaturas.AsNoTracking()
            join plano in db.Planos.AsNoTracking() on assinatura.PlanoId equals plano.Id
            orderby assinatura.CriadoEm descending, assinatura.Id descending
            select new AssinaturaDetalhe(
                assinatura.Id,
                assinatura.Status,
                new PlanoResumo(plano.Id, plano.Codigo, plano.Nome, plano.PrecoEmCentavos, plano.Ciclo, plano.LimiteDeFormandos, plano.Recomendado),
                assinatura.VigenteAte,
                assinatura.Status == StatusDaAssinatura.Ativa ? assinatura.VigenteAte : null,
                assinatura.CanceladaEm,
                assinatura.CriadoEm
            )
        ).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<Assinatura?> ObterMaisRecenteParaEdicao(CancellationToken ct = default) =>
        db.Assinaturas.OrderByDescending(a => a.CriadoEm).ThenByDescending(a => a.Id).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task Adicionar(Assinatura assinatura, CancellationToken ct = default) => await db.Assinaturas.AddAsync(assinatura, ct);

    /// <inheritdoc />
    public Task<Assinatura?> ObterParaEdicaoDeTodasAsFormaturas(Guid assinaturaId, CancellationToken ct = default) =>
        db.Assinaturas.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == assinaturaId, ct);

    /// <inheritdoc />
    /// <remarks>Mais recentes primeiro: se o lote não couber, quem acabou de pagar não espera atrás de checkout esquecido.</remarks>
    public async Task<IReadOnlyList<Assinatura>> ListarPendentesDeTodasAsFormaturas(
        DateTime atualizadasAntesDe,
        DateTime atualizadasDepoisDe,
        int limite,
        CancellationToken ct = default
    ) =>
        await db
            .Assinaturas.IgnoreQueryFilters()
            .Where(a => a.Status == StatusDaAssinatura.Pendente && a.AtualizadoEm <= atualizadasAntesDe && a.AtualizadoEm >= atualizadasDepoisDe)
            .OrderByDescending(a => a.AtualizadoEm)
            .ThenBy(a => a.Id)
            .Take(limite)
            .ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>Quem venceu há mais tempo primeiro: é quem precisa sair da lista (virar vencida) antes.</remarks>
    public async Task<IReadOnlyList<Assinatura>> ListarVencendoDeTodasAsFormaturas(
        DateTime vigentesAte,
        int limite,
        CancellationToken ct = default
    ) =>
        await db
            .Assinaturas.IgnoreQueryFilters()
            .Where(a =>
                (a.Status == StatusDaAssinatura.Ativa || a.Status == StatusDaAssinatura.Cancelada)
                && a.VigenteAte != null
                && a.VigenteAte <= vigentesAte
            )
            .OrderBy(a => a.VigenteAte)
            .ThenBy(a => a.Id)
            .Take(limite)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<bool> RegistrarSeNovo(EventoDeCobranca evento, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO eventos_de_cobranca (id, id_externo, tipo, assinatura_id, formatura_id, payload, recebido_em, processado_em)
            VALUES ({evento.Id}, {evento.IdExterno}, {evento.Tipo}, {evento.AssinaturaId}, {evento.FormaturaId}, {evento.Payload}, {evento.RecebidoEm}, {evento.ProcessadoEm})
            ON CONFLICT (id_externo) DO NOTHING
            """,
            ct
        ) == 1;
}
