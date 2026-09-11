using Backend.Business.Abstractions;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data;

/// <summary>
/// Implementação da fronteira transacional sobre o <see cref="AppDbContext"/>.
/// </summary>
/// <remarks>
/// Não há segundo rastreador nem segunda conexão: é o **mesmo** contexto scoped que os
/// repositórios usam. O tipo existe para que a camada de negócio possa pedir o commit sem
/// conhecer o EF Core.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SalvarAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Usa a estratégia de execução do provider, que reexecuta a operação inteira em falha
    /// transitória (queda de conexão, failover). Por isso a operação precisa ser idempotente:
    /// ela pode rodar mais de uma vez.
    /// <para>
    /// Se já existir transação aberta, apenas executa dentro dela — chamadas aninhadas não
    /// criam transação nova nem commitam a de fora.
    /// </para>
    /// </remarks>
    public async Task<T> EmTransacaoAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null)
            return await operacao(ct);

        var estrategia = db.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(
            async token =>
            {
                await using var transacao = await db.Database.BeginTransactionAsync(token);

                var resultado = await operacao(token);

                await db.SaveChangesAsync(token);
                await transacao.CommitAsync(token);

                return resultado;
            },
            ct
        );
    }
}
