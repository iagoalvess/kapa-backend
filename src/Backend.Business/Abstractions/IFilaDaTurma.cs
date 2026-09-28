namespace Backend.Business.Abstractions;

/// <summary>
/// A fila de escrita de uma turma: poucas escritas por vez chegam ao banco, e as demais esperam em memória,
/// em ordem de chegada.
/// </summary>
/// <remarks>
/// Em <c>Abstractions</c> porque quem a implementa é a Api (o limitador de concorrência do ASP.NET Core) e quem
/// precisa dela é o service: a loja pública (Sprint 26, decisão 8) entra na fila <b>depois</b> da leitura que
/// responde "esgotado" e sai <b>antes</b> de chamar o Mercado Pago — as duas bordas moram dentro da compra.
/// Fora da Api (worker, CLI), <see cref="SemFila"/> deixa passar tudo.
/// </remarks>
public interface IFilaDaTurma
{
    /// <summary>Espera a vez na fila da turma.</summary>
    /// <param name="formaturaId">Turma.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>A vaga, que se devolve no <c>Dispose</c>; nula quando a fila está cheia.</returns>
    Task<IDisposable?> Entrar(Guid formaturaId, CancellationToken ct = default);
}

/// <summary>Sem fila: fora da Api não há concorrência de requisições a ordenar.</summary>
public sealed class SemFila : IFilaDaTurma
{
    /// <inheritdoc />
    public Task<IDisposable?> Entrar(Guid formaturaId, CancellationToken ct = default) => Task.FromResult<IDisposable?>(Vaga.Livre);

    private sealed class Vaga : IDisposable
    {
        public static readonly Vaga Livre = new();

        public void Dispose() { }
    }
}
