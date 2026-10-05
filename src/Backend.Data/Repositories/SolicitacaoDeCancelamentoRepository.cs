using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Loja.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// As solicitações de cancelamento da formatura selecionada (Sprint 48, D8).
/// </summary>
/// <remarks>
/// O nome de quem pediu sai da mesma regra das parcelas e dos pedidos — o civil do cadastro quando houver, senão o da
/// conta —, e o pago, da soma das parcelas do par (vínculo, item), que é o recorte da solicitação.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class SolicitacaoDeCancelamentoRepository(AppDbContext db) : ISolicitacaoDeCancelamentoRepository
{
    /// <inheritdoc />
    public Task<bool> ExisteAberta(Guid vinculoId, Guid itemId, CancellationToken ct = default) =>
        db.SolicitacoesDeCancelamento.AnyAsync(
            s => s.VinculoId == vinculoId && s.ItemDeCobrancaId == itemId && s.Status == StatusDoPedidoDeCancelamento.Aberto,
            ct
        );

    /// <inheritdoc />
    /// <remarks>
    /// SQL à mão porque o EF Core não expressa <c>FOR UPDATE</c>: dois cliques em "Aprovar" esperam um pelo outro, e o
    /// segundo relê a solicitação já respondida. O filtro global da formatura é aplicado por fora do SQL.
    /// </remarks>
    public Task<SolicitacaoDeCancelamento?> Travar(Guid id, CancellationToken ct = default) =>
        db.SolicitacoesDeCancelamento.FromSql($"SELECT * FROM solicitacoes_de_cancelamento WHERE id = {id} FOR UPDATE").FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<ResumoDaSolicitacao?> Obter(Guid id, CancellationToken ct = default) =>
        Projetar(db.SolicitacoesDeCancelamento.AsNoTracking().Where(s => s.Id == id)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResumoDaSolicitacao>> Listar(
        Guid? vinculoId,
        StatusDoPedidoDeCancelamento? status,
        CancellationToken ct = default
    )
    {
        var consulta = db.SolicitacoesDeCancelamento.AsNoTracking();

        if (vinculoId is { } vinculo)
            consulta = consulta.Where(s => s.VinculoId == vinculo);

        if (status is { } situacao)
            consulta = consulta.Where(s => s.Status == situacao);

        return await Projetar(consulta.OrderByDescending(s => s.PedidoEm).ThenBy(s => s.Id)).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task Adicionar(SolicitacaoDeCancelamento solicitacao, CancellationToken ct = default) =>
        await db.SolicitacoesDeCancelamento.AddAsync(solicitacao, ct);

    /// <summary>A solicitação com o item, quem pediu e o que já entrou — a última etapa da consulta.</summary>
    /// <param name="solicitacoes">Solicitações já filtradas e ordenadas.</param>
    private IQueryable<ResumoDaSolicitacao> Projetar(IQueryable<SolicitacaoDeCancelamento> solicitacoes) =>
        from solicitacao in solicitacoes
        join item in db.ItensDeCobranca.AsNoTracking() on solicitacao.ItemDeCobrancaId equals item.Id
        join vinculo in db.Vinculos.AsNoTracking() on solicitacao.VinculoId equals vinculo.Id
        join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
        join perfil in db.PerfisDeFormandos.AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
        from perfil in perfis.DefaultIfEmpty()
        select new ResumoDaSolicitacao(
            solicitacao.Id,
            usuario.Id,
            perfil != null && perfil.NomeCompleto != null ? perfil.NomeCompleto : usuario.Nome,
            item.Id,
            item.Tipo,
            item.Descricao,
            item.Grupo,
            solicitacao.PedidoId,
            solicitacao.Motivo,
            solicitacao.PedidoEm,
            solicitacao.RespostaAte,
            solicitacao.Status,
            solicitacao.MotivoDaResposta,
            solicitacao.RespondidoEm,
            db.Parcelas.Where(p => p.VinculoId == solicitacao.VinculoId && p.ItemDeCobrancaId == solicitacao.ItemDeCobrancaId)
                .Sum(p => (long?)p.ValorPagoEmCentavos)
                ?? 0
        );
}
