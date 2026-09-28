using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Os eventos da agenda da formatura selecionada.
/// </summary>
/// <remarks>
/// Sem paginação e sem filtro de período: a agenda de uma turma inteira são dezenas de linhas, e
/// quem agrupa por mês, esconde o passado e conta o que falta confirmar é a tela, sobre a lista que
/// já chegou (decisão 10).
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class EventoDaTurmaRepository(AppDbContext db) : IEventoDaTurmaRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Por dia, depois por hora, com o id de desempate: dois eventos no mesmo dia sem hora — o caso
    /// comum — trocariam de lugar entre duas aberturas da tela sem o último critério. Quem não tem
    /// hora vem antes de quem tem, que é como a lista do dia se lê.
    /// </remarks>
    public async Task<IReadOnlyList<EventoResumo>> Listar(CancellationToken ct = default) =>
        await Projetar(db.EventosDaTurma.AsNoTracking().OrderBy(e => e.Data).ThenBy(e => e.Hora).ThenBy(e => e.Id)).ToListAsync(ct);

    /// <inheritdoc />
    public Task<EventoResumo?> Obter(Guid id, CancellationToken ct = default) =>
        Projetar(db.EventosDaTurma.AsNoTracking().Where(e => e.Id == id)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Mesma ordenação da lista, recortada pelo índice <c>(formatura_id, data)</c>. O dia de hoje
    /// entra: a reunião de hoje à noite ainda é a próxima às oito da manhã.
    /// </remarks>
    public async Task<IReadOnlyList<EventoResumo>> Proximos(DateOnly hoje, int limite, CancellationToken ct = default) =>
        await Projetar(DaquiEmDiante(hoje).AsNoTracking().OrderBy(e => e.Data).ThenBy(e => e.Hora).ThenBy(e => e.Id).Take(limite)).ToListAsync(ct);

    /// <inheritdoc />
    public Task<int> ContarDaqui(DateOnly hoje, CancellationToken ct = default) => DaquiEmDiante(hoje).CountAsync(ct);

    /// <inheritdoc />
    public Task<EventoResumo?> ObterDoTipo(TipoDeEvento tipo, CancellationToken ct = default) =>
        Projetar(db.EventosDaTurma.AsNoTracking().Where(e => e.Tipo == tipo)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<EventoDaTurma?> ObterParaEdicao(Guid id, CancellationToken ct = default) =>
        db.EventosDaTurma.FirstOrDefaultAsync(e => e.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> ExisteDoTipo(TipoDeEvento tipo, Guid? exceto, CancellationToken ct = default) =>
        db.EventosDaTurma.AsNoTracking().AnyAsync(e => e.Tipo == tipo && (exceto == null || e.Id != exceto), ct);

    /// <inheritdoc />
    public async Task Adicionar(EventoDaTurma evento, CancellationToken ct = default) => await db.EventosDaTurma.AddAsync(evento, ct);

    /// <inheritdoc />
    public void Remover(EventoDaTurma evento) => db.EventosDaTurma.Remove(evento);

    /// <summary>O que ainda vai acontecer: de hoje em diante, sem o que foi desmarcado.</summary>
    /// <param name="hoje">Dia de hoje no fuso da turma.</param>
    private IQueryable<EventoDaTurma> DaquiEmDiante(DateOnly hoje) =>
        db.EventosDaTurma.Where(e => e.Data >= hoje && e.Situacao != SituacaoDoEvento.Cancelado);

    /// <summary>O evento como a tela o desenha.</summary>
    /// <param name="consulta">Eventos já filtrados e ordenados.</param>
    private static IQueryable<EventoResumo> Projetar(IQueryable<EventoDaTurma> consulta) =>
        consulta.Select(e => new EventoResumo(e.Id, e.Titulo, e.Tipo, e.Situacao, e.Data, e.Hora, e.Local, e.Descricao));
}
