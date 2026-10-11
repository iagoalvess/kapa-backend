using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>Travas dos contextos Google, compartilhadas por todas as réplicas da API.</summary>
public sealed class TentativaGoogleRepository(AppDbContext db) : ITentativaGoogleRepository
{
    /// <inheritdoc />
    public async Task Adicionar(TentativaGoogle tentativa, CancellationToken ct = default) => await db.Set<TentativaGoogle>().AddAsync(tentativa, ct);

    /// <inheritdoc />
    public Task<TentativaGoogle?> Travar(string hash, CancellationToken ct = default) =>
        db.Set<TentativaGoogle>().FromSql($"SELECT * FROM tentativas_google WHERE token_hash = {hash} FOR UPDATE").FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>Trava transacional, sem gravar entidade ou commitar; o serviço conserva a fronteira.</remarks>
    public Task TravarIdentidade(string chave, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({chave}, 0))", ct);

    /// <inheritdoc />
    /// <remarks>Limpeza em massa do worker, sem outras mudanças para compor na transação.</remarks>
    public Task<int> RemoverExpiradas(DateTime limiteUtc, CancellationToken ct = default) =>
        db.Set<TentativaGoogle>().Where(x => x.ExpiraEm < limiteUtc).ExecuteDeleteAsync(ct);
}
