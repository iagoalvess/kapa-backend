using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.IntegrationTests.Infra;

/// <summary>
/// Conta os comandos SQL que uma requisição dispara, para a guarda de N+1.
/// </summary>
/// <remarks>
/// Só de teste: registrado no <see cref="ApiFactory"/> e zerado pelo teste antes da rota. Em
/// produção quem mostra o SQL real é o trace do Npgsql; aqui a pergunta é outra — se uma rota quente
/// passou a fazer uma consulta por item.
/// </remarks>
public sealed class ContadorDeComandos : DbCommandInterceptor
{
    private int _comandos;

    /// <summary>Quantos comandos rodaram desde o último <see cref="Zerar"/>.</summary>
    public int Comandos => Volatile.Read(ref _comandos);

    /// <summary>Zera o contador antes da rota medida.</summary>
    public void Zerar() => Interlocked.Exchange(ref _comandos, 0);

    /// <inheritdoc />
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        Interlocked.Increment(ref _comandos);
        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Interlocked.Increment(ref _comandos);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Interlocked.Increment(ref _comandos);
        return base.ScalarExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        Interlocked.Increment(ref _comandos);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Interlocked.Increment(ref _comandos);
        return base.NonQueryExecuting(command, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Interlocked.Increment(ref _comandos);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }
}
