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

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListarCesta(Guid vinculoId, CancellationToken ct = default) =>
        await db.EscolhasDaCesta.AsNoTracking().Where(e => e.VinculoId == vinculoId).Select(e => e.ItemDeCobrancaId).ToListAsync(ct);

    /// <inheritdoc />
    public Task AdicionarEscolhas(IEnumerable<EscolhaDaCesta> escolhas, CancellationToken ct = default) =>
        db.EscolhasDaCesta.AddRangeAsync(escolhas, ct);

    /// <inheritdoc />
    public Task<EscolhaDaCesta?> ObterEscolhaParaEdicao(Guid vinculoId, Guid itemId, CancellationToken ct = default) =>
        db.EscolhasDaCesta.FirstOrDefaultAsync(e => e.VinculoId == vinculoId && e.ItemDeCobrancaId == itemId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<EscolhaDaCesta>> ListarEscolhas(Guid vinculoId, CancellationToken ct = default) =>
        await db.EscolhasDaCesta.AsNoTracking().Where(e => e.VinculoId == vinculoId).ToListAsync(ct);

    /// <inheritdoc />
    public void RemoverEscolha(EscolhaDaCesta escolha) => db.EscolhasDaCesta.Remove(escolha);

    /// <inheritdoc />
    /// <remarks>O nome sai da mesma regra das parcelas: o civil do cadastro quando houver, senão o da conta.</remarks>
    public async Task<IReadOnlyList<LancamentoResumo>> ListarLancamentos(CancellationToken ct = default)
    {
        var linhas = await (
            from item in db.ItensDeCobranca.AsNoTracking()
            join vinculo in db.Vinculos.AsNoTracking() on item.VinculoDoLancamento equals (Guid?)vinculo.Id
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            join perfil in db.PerfisDeFormandos.AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
            from perfil in perfis.DefaultIfEmpty()
            orderby item.CriadoEm descending, item.Id
            select new
            {
                Item = item,
                UsuarioId = usuario.Id,
                Nome = perfil != null && perfil.NomeCompleto != null ? perfil.NomeCompleto : usuario.Nome,
                Pago = db.Parcelas.Where(p => p.ItemDeCobrancaId == item.Id).Sum(p => (long?)p.ValorPagoEmCentavos) ?? 0,
            }
        ).ToListAsync(ct);

        return
        [
            .. linhas.Select(linha => new LancamentoResumo(
                linha.Item.Id,
                linha.Item.PlanoId,
                linha.UsuarioId,
                linha.Nome,
                linha.Item.Descricao,
                linha.Item.ValorEmCentavos,
                linha.Item.NumeroDeParcelas,
                GradeDeParcelas.Vencimento(linha.Item.PrimeiroMes, linha.Item.DiaDeVencimento),
                linha.Item.CriadoEm,
                linha.Item.EncerradoEm,
                linha.Pago
            )),
        ];
    }
}
