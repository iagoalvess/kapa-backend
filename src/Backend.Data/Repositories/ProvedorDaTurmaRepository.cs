using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// A autorização do Mercado Pago da turma e os PIX dinâmicos emitidos com ela.
/// </summary>
/// <param name="db">Contexto.</param>
public sealed class ProvedorDaTurmaRepository(AppDbContext db) : IProvedorDaTurmaRepository
{
    /// <inheritdoc />
    public Task<ProvedorConectado?> ObterConexao(CancellationToken ct = default) =>
        (
            from credencial in db.CredenciaisDeProvedor.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on credencial.CadastradaPorUsuarioId equals usuario.Id into autores
            from autor in autores.DefaultIfEmpty()
            select new ProvedorConectado(credencial.ContaNoProvedor, credencial.AtualizadoEm, autor == null ? null : autor.Nome)
        ).SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<CredencialDeProvedor?> ObterCredencial(CancellationToken ct = default) => db.CredenciaisDeProvedor.SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task AdicionarCredencial(CredencialDeProvedor credencial, CancellationToken ct = default) =>
        await db.CredenciaisDeProvedor.AddAsync(credencial, ct);

    /// <inheritdoc />
    public void RemoverCredencial(CredencialDeProvedor credencial) => db.CredenciaisDeProvedor.Remove(credencial);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListarFormaturasComCredencialAVencerDeTodasAsFormaturas(
        DateTime ate,
        int limite,
        CancellationToken ct = default
    ) =>
        await db
            .CredenciaisDeProvedor.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.ExpiraEm <= ate)
            .OrderBy(c => c.ExpiraEm)
            .Select(c => c.FormaturaId)
            .Take(limite)
            .ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// O alvo do <c>ON CONFLICT</c> repete o predicado do índice parcial — sem ele, o Postgres não acha o
    /// índice e recusa a instrução. A formatura vem da sessão, porque o carimbo do contexto não alcança SQL
    /// à mão.
    /// </remarks>
    public async Task<bool> ReservarEmissao(CobrancaBancaria cobranca, CancellationToken ct = default)
    {
        var formaturaId = db.FormaturaAtualId ?? throw new InvalidOperationException("Emissão de cobrança sem formatura selecionada.");
        var agora = DateTime.UtcNow;
        var status = cobranca.Status.ToString();
        var meio = cobranca.Meio.ToString();

        var gravadas = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO cobrancas_bancarias (id, formatura_id, meio, conta_no_provedor, parcela_ids, compra_id, chave, valor_em_centavos, status, expira_em, criado_em, atualizado_em)
            VALUES ({cobranca.Id}, {formaturaId}, {meio}, {cobranca.ContaNoProvedor}, {cobranca.ParcelaIds}, {cobranca.CompraId}, {cobranca.Chave}, {cobranca.ValorEmCentavos}, {status}, {cobranca.ExpiraEm}, {agora}, {agora})
            ON CONFLICT (formatura_id, chave) WHERE status IN ('Emitindo', 'Emitida') DO NOTHING
            """,
            ct
        );

        return gravadas == 1;
    }

    /// <inheritdoc />
    public Task<CobrancaBancaria?> ObterViva(string chave, CancellationToken ct = default) =>
        db
            .CobrancasBancarias.AsNoTracking()
            .Where(c => c.Chave == chave && (c.Status == StatusDaCobrancaBancaria.Emitindo || c.Status == StatusDaCobrancaBancaria.Emitida))
            .SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<CobrancaBancaria?> ObterCobranca(Guid id, CancellationToken ct = default) =>
        db.CobrancasBancarias.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

    /// <inheritdoc />
    public Task<CobrancaBancaria?> ObterCobrancaParaEdicao(Guid id, CancellationToken ct = default) =>
        db.CobrancasBancarias.SingleOrDefaultAsync(c => c.Id == id, ct);

    /// <inheritdoc />
    public Task<CobrancaBancaria?> TravarCobranca(Guid id, CancellationToken ct = default) =>
        db.CobrancasBancarias.FromSql($"SELECT * FROM cobrancas_bancarias WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<CobrancaAConciliar?> ObterPorIdExternoDeTodasAsFormaturas(string idExterno, CancellationToken ct = default) =>
        db
            .CobrancasBancarias.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.IdExterno == idExterno)
            .Select(c => new CobrancaAConciliar(c.Id, c.FormaturaId))
            .SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CobrancaAConciliar>> ListarAConciliarDeTodasAsFormaturas(
        DateTime emitidasAntesDe,
        int limite,
        CancellationToken ct = default
    ) =>
        await db
            .CobrancasBancarias.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Status == StatusDaCobrancaBancaria.Emitida && c.CriadoEm < emitidasAntesDe)
            .OrderBy(c => c.CriadoEm)
            .Select(c => new CobrancaAConciliar(c.Id, c.FormaturaId))
            .Take(limite)
            .ToListAsync(ct);
}
