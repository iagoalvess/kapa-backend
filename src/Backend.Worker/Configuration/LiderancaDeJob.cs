using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Worker.Configuration;

/// <summary>
/// Garante que só uma réplica do worker rode cada job por vez.
/// </summary>
/// <remarks>
/// Os jobs listam o que há para fazer e depois fazem, sem reservar nada no caminho: com duas réplicas,
/// as duas geravam o mesmo PDF, exportavam os mesmos dados do titular e mandavam os e-mails em dobro, e
/// a régua refazia cada turma só para bater no índice único e logar erro. A trava é um
/// <c>pg_try_advisory_lock</c> numa conexão aberta só para ela: quem não pega pula a passada, e a trava
/// cai sozinha se o processo morrer — a conexão fecha junto.
/// </remarks>
/// <param name="scopeFactory">Escopo de onde sai a conexão da trava.</param>
public sealed class LiderancaDeJob(IServiceScopeFactory scopeFactory)
{
    /// <summary>Tenta assumir o job; <c>null</c> se outra réplica já está com ele.</summary>
    /// <param name="job">Nome estável do job — a chave da trava.</param>
    /// <returns>A trava, a devolver com <c>await using</c> ao fim da passada.</returns>
    public async Task<IAsyncDisposable?> Assumir(string job, CancellationToken ct = default)
    {
        var escopo = scopeFactory.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.OpenConnectionAsync(ct);

        var assumiu = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock(hashtextextended({job}, 0)) AS \"Value\"").SingleAsync(ct);

        if (assumiu)
            return new Trava(escopo, db, job);

        await escopo.DisposeAsync();

        return null;
    }

    /// <summary>A trava em posse desta réplica, com a conexão que a segura.</summary>
    private sealed class Trava(AsyncServiceScope escopo, AppDbContext db, string job) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock(hashtextextended({job}, 0)) AS \"Value\"").SingleAsync();
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
                await escopo.DisposeAsync();
            }
        }
    }
}
