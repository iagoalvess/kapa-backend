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
    /// <para>
    /// Operação que devolve um <see cref="Result"/> de falha <b>não</b> é commitada: falha
    /// prevista não lança exceção, então sem esta checagem os passos anteriores a ela seriam
    /// gravados — o cadastro deixaria a conta criada quando o aceite fosse recusado.
    /// </para>
    /// <para>
    /// A cada tentativa, e depois de um rollback, o rastreador volta a ter só o que já tinha antes da
    /// transação (<see cref="Descartar"/>): sem isso, a nova tentativa gravava de novo o que a anterior
    /// adicionou — recebimento e e-mail em dobro — e lia as parcelas travadas do cache, sem o que outra
    /// transação acabou de gravar. O commit usa <c>SaveChanges(false)</c> + <c>AcceptAllChanges</c>, para
    /// uma falha entre gravar e commitar não dar as mudanças por aceitas.
    /// </para>
    /// </remarks>
    public async Task<T> EmTransacaoAsync<T>(Func<CancellationToken, Task<T>> operacao, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is not null)
            return await operacao(ct);

        var rastreadasAntes = db.ChangeTracker.Entries().Select(entrada => entrada.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        var estrategia = db.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(
            async token =>
            {
                Descartar(rastreadasAntes);

                await using var transacao = await db.Database.BeginTransactionAsync(token);

                var resultado = await operacao(token);

                if (resultado is Result { Falhou: true })
                {
                    await transacao.RollbackAsync(token);
                    Descartar(rastreadasAntes);
                    return resultado;
                }

                await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, token);
                await transacao.CommitAsync(token);
                db.ChangeTracker.AcceptAllChanges();

                return resultado;
            },
            ct
        );
    }

    /// <summary>Solta do rastreador tudo o que entrou nele depois de <paramref name="rastreadasAntes"/>.</summary>
    /// <param name="rastreadasAntes">O que já estava rastreado quando a transação começou.</param>
    private void Descartar(HashSet<object> rastreadasAntes)
    {
        foreach (var entrada in db.ChangeTracker.Entries().Where(entrada => !rastreadasAntes.Contains(entrada.Entity)).ToList())
            entrada.State = EntityState.Detached;
    }
}
