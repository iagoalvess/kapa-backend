using System.Threading.RateLimiting;
using Backend.Business.Abstractions;

namespace Backend.Api.Configuration;

/// <summary>
/// A fila de escrita por turma: <c>FilaPorTurmaSimultaneas</c> escritas de cada turma chegam ao banco, e as
/// demais esperam em memória, em ordem de chegada, até <c>FilaPorTurmaEspera</c>.
/// </summary>
/// <remarks>
/// Uma instância só, com duas portas. O limitador global a usa para as rotas <see cref="FilaPorTurmaAttribute"/>,
/// antes do controller; a loja pública (Sprint 26, decisão 8) entra nela pelo <see cref="IFilaDaTurma"/>, dentro do service, depois da
/// leitura sem trava que responde "esgotado" — senão os mil que perdem esperariam na fila para descobrir que
/// acabou, e a fila cheia devolveria 429 no lugar de "esgotado". As duas portas dividem as mesmas vagas: a
/// abertura da loja e o pedido do formando da mesma turma disputam a mesma linha do item.
/// </remarks>
public sealed class FilaPorTurma : IFilaDaTurma, IDisposable
{
    private readonly PartitionedRateLimiter<string> _limitador;

    /// <summary>Monta a fila.</summary>
    /// <param name="simultaneas">Escritas por turma que chegam ao banco ao mesmo tempo.</param>
    /// <param name="espera">Quantas esperam na fila; além disso, 429.</param>
    public FilaPorTurma(int simultaneas, int espera) =>
        _limitador = PartitionedRateLimiter.Create<string, string>(chave =>
            chave.Length == 0
                ? RateLimitPartition.GetNoLimiter(string.Empty)
                : RateLimitPartition.GetConcurrencyLimiter(
                    chave,
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = simultaneas,
                        QueueLimit = espera,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    }
                )
        );

    /// <summary>A chave da fila de uma turma.</summary>
    /// <param name="formaturaId">Turma.</param>
    public static string DaTurma(Guid formaturaId) => $"turma:{formaturaId}";

    /// <summary>A mesma fila vista pelo limitador global; chave vazia passa sem limite.</summary>
    /// <param name="chave">A chave da requisição.</param>
    public PartitionedRateLimiter<HttpContext> ParaRequisicoes(Func<HttpContext, string> chave) =>
        _limitador.WithTranslatedKey(chave, leaveOpen: true);

    /// <inheritdoc />
    public async Task<IDisposable?> Entrar(Guid formaturaId, CancellationToken ct = default)
    {
        var vaga = await _limitador.AcquireAsync(DaTurma(formaturaId), 1, ct);

        if (vaga.IsAcquired)
            return vaga;

        vaga.Dispose();
        return null;
    }

    /// <inheritdoc />
    public void Dispose() => _limitador.Dispose();
}
