using Backend.Business.Agenda.Models;
using Backend.Business.Assinaturas.Models;
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
    /// <remarks>
    /// As duas datas saem da agenda (Sprint 19, decisão 1), por subconsulta correlacionada, ao lado
    /// da que já existia para <c>JaContratou</c>. O contrato não mudou — é o que faz o contador do
    /// Início, os três marcos e a janela da projeção do caixa seguirem certos sem uma linha de
    /// alteração neles — e nada mais no produto lê estas datas de outro lugar.
    /// <para>
    /// A cláusula de formatura é explícita <b>além</b> do filtro global: sem ela, chamar este método
    /// com uma turma diferente da que está na sessão devolveria a data da turma da sessão no detalhe
    /// da outra. O filtro global sozinho não repara nisso, porque quem varia aqui é o parâmetro.
    /// </para>
    /// </remarks>
    public Task<FormaturaDetalhe?> ObterDetalheDeTodasAsFormaturas(Guid formaturaId, CancellationToken ct = default) =>
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
                db.EventosDaTurma.Where(e => e.FormaturaId == f.Id && e.Tipo == TipoDeEvento.Colacao).Select(e => (DateOnly?)e.Data).FirstOrDefault(),
                db.EventosDaTurma.Where(e => e.FormaturaId == f.Id && e.Tipo == TipoDeEvento.Festa).Select(e => (DateOnly?)e.Data).FirstOrDefault(),
                f.Status,
                f.EncerradaEm,
                db.Assinaturas.IgnoreQueryFilters().Any(a => a.FormaturaId == f.Id && a.Status != StatusDaAssinatura.Pendente)
            ))
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<string?> ObterNome(Guid formaturaId, CancellationToken ct = default) =>
        db.Formaturas.AsNoTracking().Where(f => f.Id == formaturaId).Select(f => (string?)f.Nome).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<StatusDaFormatura?> ObterStatus(Guid formaturaId, CancellationToken ct = default) =>
        db.Formaturas.AsNoTracking().Where(f => f.Id == formaturaId).Select(f => (StatusDaFormatura?)f.Status).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<Formatura?> ObterParaEdicao(Guid formaturaId, CancellationToken ct = default) =>
        db.Formaturas.FirstOrDefaultAsync(f => f.Id == formaturaId, ct);

    /// <inheritdoc />
    public Task<bool> ExisteGratuitaCriadaPorDeTodasAsFormaturas(Guid usuarioId, CancellationToken ct = default) =>
        db.Formaturas.AnyAsync(
            f =>
                f.CriadoPorUsuarioId == usuarioId
                && f.Status != StatusDaFormatura.Descartada
                && f.Status != StatusDaFormatura.Encerrada
                && !db.Assinaturas.IgnoreQueryFilters().Any(a => a.FormaturaId == f.Id && a.Status != StatusDaAssinatura.Pendente),
            ct
        );

    /// <inheritdoc />
    public async Task Adicionar(Formatura formatura, CancellationToken ct = default) => await db.Formaturas.AddAsync(formatura, ct);
}
