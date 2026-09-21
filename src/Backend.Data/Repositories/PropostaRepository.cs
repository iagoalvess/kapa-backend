using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// As propostas dos itens da festa e os votos nelas.
/// </summary>
/// <remarks>
/// O placar vem por subconsulta correlacionada, como as somas de despesa do
/// <c>ItemDaFestaRepository</c> e pelo mesmo motivo: são poucas propostas por item, e assim a lista
/// continua numa consulta só. Não há coluna de contador — placar gravado é placar que um dia alguém
/// esquece de decrementar, e aí a tela que a turma inteira olha mente.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class PropostaRepository(AppDbContext db) : IPropostaRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Da mais votada para a menos, com o valor e o id de desempate: sem eles, duas propostas
    /// empatadas em zero voto — que é como toda disputa começa — trocariam de lugar entre duas
    /// aberturas da tela.
    /// <para>
    /// A ordenação acontece <b>antes</b> do <see cref="Projetar"/>, e repete a subconsulta do placar
    /// em vez de ordenar por <c>PropostaResumo.Votos</c>. Ordenar pela propriedade do record obriga
    /// o EF a traduzir <c>OrderByDescending(p =&gt; new PropostaResumo(…).Votos)</c>, que ele não
    /// sabe fazer, e a consulta estoura em tempo de execução — nenhum teste unitário pega isso.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<PropostaResumo>> Listar(Guid itemId, Guid? vinculoId, CancellationToken ct = default) =>
        await Projetar(
                db.PropostasDoItem.AsNoTracking()
                    .Where(p => p.ItemDaFestaId == itemId)
                    .OrderByDescending(p => db.VotosNasPropostas.Count(v => v.PropostaId == p.Id))
                    .ThenBy(p => p.ValorEmCentavos)
                    .ThenBy(p => p.Id),
                vinculoId
            )
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<PropostaResumo?> Obter(Guid id, Guid? vinculoId, CancellationToken ct = default) =>
        Projetar(db.PropostasDoItem.AsNoTracking().Where(p => p.Id == id), vinculoId).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<PropostaDoItem?> ObterParaEdicao(Guid id, CancellationToken ct = default) =>
        db.PropostasDoItem.FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <inheritdoc />
    public Task<VotoNaProposta?> ObterVoto(Guid vinculoId, Guid itemId, CancellationToken ct = default) =>
        db.VotosNasPropostas.FirstOrDefaultAsync(v => v.VinculoId == vinculoId && v.ItemDaFestaId == itemId, ct);

    /// <inheritdoc />
    public async Task Adicionar(PropostaDoItem proposta, CancellationToken ct = default) => await db.PropostasDoItem.AddAsync(proposta, ct);

    /// <inheritdoc />
    public async Task AdicionarVoto(VotoNaProposta voto, CancellationToken ct = default) => await db.VotosNasPropostas.AddAsync(voto, ct);

    /// <inheritdoc />
    public void Remover(PropostaDoItem proposta) => db.PropostasDoItem.Remove(proposta);

    /// <inheritdoc />
    public void RemoverVoto(VotoNaProposta voto) => db.VotosNasPropostas.Remove(voto);

    /// <summary>
    /// A proposta com o placar e a marca de quem está lendo.
    /// </summary>
    /// <remarks>
    /// Sem vínculo — a Gestão que não é formando da turma — <c>MeuVoto</c> é falso em todas, e a
    /// comparação sai da consulta: <c>vinculoId != null &amp;&amp; …</c> é avaliado pelo EF na
    /// tradução, não linha a linha.
    /// </remarks>
    /// <param name="consulta">Propostas já filtradas.</param>
    /// <param name="vinculoId">Quem está lendo.</param>
    private IQueryable<PropostaResumo> Projetar(IQueryable<PropostaDoItem> consulta, Guid? vinculoId) =>
        consulta.Select(p => new PropostaResumo(
            p.Id,
            p.Titulo,
            p.ValorEmCentavos,
            p.OQueInclui,
            db.VotosNasPropostas.Count(v => v.PropostaId == p.Id),
            vinculoId != null && db.VotosNasPropostas.Any(v => v.PropostaId == p.Id && v.VinculoId == vinculoId)
        ));
}
