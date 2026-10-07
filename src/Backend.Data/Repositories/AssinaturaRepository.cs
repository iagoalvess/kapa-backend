using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Data.Context;
using Backend.Data.Seed;
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
            .Select(p => new PlanoResumo(
                p.Id,
                p.Codigo,
                p.Nome,
                p.Descricao,
                p.PrecoEmCentavos,
                p.PrecoCheioEmCentavos,
                p.Ciclo,
                p.LimiteDeFormandos,
                p.Modulos,
                p.Recomendado
            ))
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<Plano?> ObterPlanoAtivo(string codigo, CancellationToken ct = default) =>
        db.Planos.AsNoTracking().FirstOrDefaultAsync(p => p.Codigo == codigo && p.Ativo, ct);

    /// <inheritdoc />
    public Task<Plano?> ObterPlano(Guid planoId, CancellationToken ct = default) =>
        db.Planos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planoId, ct);

    /// <inheritdoc />
    /// <remarks>
    /// A próxima cobrança só existe na ativa. No cartão é o débito automático; no PIX, o vencimento do PIX do
    /// ciclo — a data é a mesma, o fim da vigência.
    /// </remarks>
    public Task<AssinaturaDetalhe?> ObterDetalheDaMaisRecente(CancellationToken ct = default) =>
        (
            from assinatura in db.Assinaturas.AsNoTracking()
            join plano in db.Planos.AsNoTracking() on assinatura.PlanoId equals plano.Id
            join proximo in db.Planos.AsNoTracking() on assinatura.PlanoDoProximoCicloId equals proximo.Id into proximos
            from proximo in proximos.DefaultIfEmpty()
            join cupom in db.Cupons.AsNoTracking() on assinatura.CupomId equals cupom.Id into cupons
            from cupom in cupons.DefaultIfEmpty()
            orderby assinatura.CriadoEm descending, assinatura.Id descending
            select new AssinaturaDetalhe(
                assinatura.Id,
                assinatura.Status,
                new PlanoResumo(
                    plano.Id,
                    plano.Codigo,
                    plano.Nome,
                    plano.Descricao,
                    plano.PrecoEmCentavos,
                    plano.PrecoCheioEmCentavos,
                    plano.Ciclo,
                    plano.LimiteDeFormandos,
                    plano.Modulos,
                    plano.Recomendado
                ),
                assinatura.VigenteAte,
                assinatura.Status == StatusDaAssinatura.Ativa ? assinatura.VigenteAte : null,
                assinatura.CanceladaEm,
                assinatura.Meio,
                proximo == null
                    ? null
                    : new PlanoResumo(
                        proximo.Id,
                        proximo.Codigo,
                        proximo.Nome,
                        proximo.Descricao,
                        proximo.PrecoEmCentavos,
                        proximo.PrecoCheioEmCentavos,
                        proximo.Ciclo,
                        proximo.LimiteDeFormandos,
                        proximo.Modulos,
                        proximo.Recomendado
                    ),
                assinatura.Meio == MeioDePagamento.Pix && assinatura.IdExterno != null,
                cupom == null ? null : new CupomAplicavel(cupom.Codigo, cupom.Percentual)
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
    public Task<Assinatura?> ObterMaisRecenteParaEdicaoDeTodasAsFormaturas(Guid formaturaId, CancellationToken ct = default) =>
        db
            .Assinaturas.IgnoreQueryFilters()
            .Where(a => a.FormaturaId == formaturaId)
            .OrderByDescending(a => a.CriadoEm)
            .ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<bool> ExisteAlgumaDeTodasAsFormaturas(Guid formaturaId, CancellationToken ct = default) =>
        db.Assinaturas.AsNoTracking().IgnoreQueryFilters().AnyAsync(a => a.FormaturaId == formaturaId && a.Status != StatusDaAssinatura.Pendente, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Só <c>Ativa</c> e <c>Cancelada</c> valem. <c>Pendente</c> de fora é o que impede o checkout de
    /// virar o próprio pagamento: quem abre a sessão do Premium e nunca paga ficaria com os
    /// módulos dele. <c>Cancelada</c> entra porque a vigência paga é respeitada até o fim, como o
    /// diálogo de cancelamento promete; <c>Vencida</c> cai no gratuito, e a turma suspensa continua
    /// dando baixa no que já entrou, que é módulo do gratuito.
    /// </remarks>
    public async Task<Plano?> ObterPlanoVigenteDeTodasAsFormaturas(Guid formaturaId, CancellationToken ct = default) =>
        await (
            from assinatura in db.Assinaturas.AsNoTracking().IgnoreQueryFilters()
            join plano in db.Planos.AsNoTracking() on assinatura.PlanoId equals plano.Id
            where
                assinatura.FormaturaId == formaturaId
                && (assinatura.Status == StatusDaAssinatura.Ativa || assinatura.Status == StatusDaAssinatura.Cancelada)
            orderby assinatura.CriadoEm descending, assinatura.Id descending
            select plano
        ).FirstOrDefaultAsync(ct) ?? await ObterPlanoPorCodigo(SeedDePlanos.CodigoGratuito, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Uma consulta: na turma suspensa vale também a assinatura <c>Vencida</c> — a mais recente que não seja checkout
    /// por pagar, que é a que a suspendeu. Fora da suspensão a regra é a de <see cref="ObterPlanoVigenteDeTodasAsFormaturas"/>.
    /// </remarks>
    public async Task<Plano?> ObterPlanoDosModulosDeTodasAsFormaturas(Guid formaturaId, CancellationToken ct = default) =>
        await (
            from assinatura in db.Assinaturas.AsNoTracking().IgnoreQueryFilters()
            join plano in db.Planos.AsNoTracking() on assinatura.PlanoId equals plano.Id
            where
                assinatura.FormaturaId == formaturaId
                && (
                    assinatura.Status == StatusDaAssinatura.Ativa
                    || assinatura.Status == StatusDaAssinatura.Cancelada
                    || (
                        assinatura.Status == StatusDaAssinatura.Vencida
                        && db.Formaturas.Any(f => f.Id == formaturaId && f.Status == StatusDaFormatura.Suspensa)
                    )
                )
            orderby assinatura.CriadoEm descending, assinatura.Id descending
            select plano
        ).FirstOrDefaultAsync(ct) ?? await ObterPlanoPorCodigo(SeedDePlanos.CodigoGratuito, ct);

    /// <summary>O plano do catálogo pelo código, inclusive o que não está mais à venda.</summary>
    /// <remarks>
    /// Sem o filtro de <c>Ativo</c>, diferente de <see cref="ObterPlanoAtivo"/>: o gratuito é
    /// <c>Ativo = false</c> justamente para não aparecer na vitrine nem ser aceito no checkout, e
    /// ainda assim é ele que responde por toda turma que não contratou.
    /// </remarks>
    /// <param name="codigo">Código do plano.</param>
    private Task<Plano?> ObterPlanoPorCodigo(string codigo, CancellationToken ct) =>
        db.Planos.AsNoTracking().FirstOrDefaultAsync(p => p.Codigo == codigo, ct);

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
    public async Task AdicionarCobranca(CobrancaDaAssinatura cobranca, CancellationToken ct = default) =>
        await db.CobrancasDaAssinatura.AddAsync(cobranca, ct);

    /// <inheritdoc />
    /// <remarks>A junção com a assinatura é o que prende a leitura à formatura da sessão.</remarks>
    public Task<CobrancaDaAssinatura?> ObterCobrancaAbertaParaEdicao(Guid assinaturaId, MotivoDaCobranca motivo, CancellationToken ct = default) =>
        (
            from cobranca in db.CobrancasDaAssinatura
            join assinatura in db.Assinaturas on cobranca.AssinaturaId equals assinatura.Id
            where cobranca.AssinaturaId == assinaturaId && cobranca.Motivo == motivo && cobranca.Situacao == SituacaoDaCobrancaDoPlano.Aberta
            orderby cobranca.CriadoEm descending
            select cobranca
        ).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>A junção com a assinatura é o que prende a leitura à formatura da sessão.</remarks>
    public async Task<IReadOnlyList<CobrancaDoPlanoResumo>> ListarCobrancas(CancellationToken ct = default) =>
        await (
            from cobranca in db.CobrancasDaAssinatura.AsNoTracking()
            join assinatura in db.Assinaturas.AsNoTracking() on cobranca.AssinaturaId equals assinatura.Id
            join plano in db.Planos.AsNoTracking() on cobranca.PlanoId equals plano.Id
            orderby cobranca.CriadoEm descending, cobranca.Id descending
            select new CobrancaDoPlanoResumo(
                cobranca.Id,
                plano.Nome,
                cobranca.Motivo,
                cobranca.Meio,
                cobranca.ValorEmCentavos,
                cobranca.Situacao,
                cobranca.Url,
                cobranca.CriadoEm,
                cobranca.PagaEm,
                cobranca.ValorEstornadoEmCentavos,
                cobranca.EstornadaEm
            )
        ).ToListAsync(ct);

    /// <inheritdoc />
    public Task<CobrancaDaAssinatura?> ObterCobrancaParaEdicaoDeTodasAsFormaturas(Guid cobrancaId, CancellationToken ct = default) =>
        db.CobrancasDaAssinatura.FirstOrDefaultAsync(c => c.Id == cobrancaId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CobrancaDaAssinatura>> ListarCobrancasAbertasDeTodasAsFormaturas(
        DateTime criadasAntesDe,
        DateTime criadasDepoisDe,
        int limite,
        CancellationToken ct = default
    ) =>
        await db
            .CobrancasDaAssinatura.Where(c =>
                c.Situacao == SituacaoDaCobrancaDoPlano.Aberta && c.CriadoEm <= criadasAntesDe && c.CriadoEm >= criadasDepoisDe
            )
            .OrderByDescending(c => c.CriadoEm)
            .ThenBy(c => c.Id)
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

    /// <inheritdoc />
    public Task<DateTime?> UltimoEventoRecebidoDeTodasAsFormaturas(CancellationToken ct = default) =>
        db.EventosDeCobranca.AsNoTracking().MaxAsync(e => (DateTime?)e.RecebidoEm, ct);
}
