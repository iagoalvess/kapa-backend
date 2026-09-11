namespace Backend.Business.Abstractions;

/// <summary>
/// Resultado de uma operação de negócio que pode falhar de forma prevista.
/// </summary>
/// <remarks>
/// Todo método público de service devolve <see cref="Result"/> ou <see cref="Result{T}"/>.
/// Exceção é reservada para o que ninguém previu (banco fora, bug) — ver <c>docs/arquitetura.md</c>.
/// </remarks>
public class Result
{
    private static readonly IReadOnlyList<Erro> SemErros = [];

    /// <summary>Inicializa um resultado com o estado e a lista de erros informados.</summary>
    protected Result(bool sucesso, IReadOnlyList<Erro> erros)
    {
        if (sucesso && erros.Count > 0)
            throw new ArgumentException("Um resultado de sucesso não pode carregar erros.", nameof(erros));

        if (!sucesso && erros.Count == 0)
            throw new ArgumentException("Um resultado de falha precisa de ao menos um erro.", nameof(erros));

        Sucesso = sucesso;
        Erros = erros;
    }

    /// <summary>Indica que a operação foi concluída.</summary>
    public bool Sucesso { get; }

    /// <summary>Indica que a operação falhou.</summary>
    public bool Falhou => !Sucesso;

    /// <summary>Erros acumulados. Vazio quando <see cref="Sucesso"/> é verdadeiro.</summary>
    public IReadOnlyList<Erro> Erros { get; }

    /// <summary>Primeiro erro da lista. Só acesse quando <see cref="Falhou"/> for verdadeiro.</summary>
    public Erro PrimeiroErro => Erros.Count > 0 ? Erros[0] : throw new InvalidOperationException("Um resultado de sucesso não possui erro.");

    /// <summary>Resultado de sucesso sem valor.</summary>
    public static Result Ok() => new(true, SemErros);

    /// <summary>Resultado de falha com um único erro.</summary>
    public static Result Falha(Erro erro) => new(false, [erro]);

    /// <summary>Resultado de falha com vários erros — usado por validação de payload.</summary>
    public static Result Falha(IEnumerable<Erro> erros) => new(false, [.. erros]);

    /// <summary>Resultado de sucesso carregando um valor.</summary>
    public static Result<T> Ok<T>(T valor) => Result<T>.DeSucesso(valor);

    /// <summary>Resultado de falha tipado com um único erro.</summary>
    public static Result<T> Falha<T>(Erro erro) => Result<T>.DeFalha([erro]);

    /// <summary>Resultado de falha tipado com vários erros.</summary>
    public static Result<T> Falha<T>(IEnumerable<Erro> erros) => Result<T>.DeFalha([.. erros]);
}

/// <summary>
/// Resultado de uma operação que devolve um valor quando bem-sucedida.
/// </summary>
/// <typeparam name="T">Tipo do valor produzido.</typeparam>
public sealed class Result<T> : Result
{
    private readonly T? _valor;

    private Result(bool sucesso, T? valor, IReadOnlyList<Erro> erros)
        : base(sucesso, erros)
    {
        _valor = valor;
    }

    /// <summary>Valor produzido. Lança se o resultado for de falha.</summary>
    public T Valor => Sucesso ? _valor! : throw new InvalidOperationException($"Não há valor: a operação falhou com '{PrimeiroErro.Codigo}'.");

    internal static Result<T> DeSucesso(T valor) => new(true, valor, []);

    internal static Result<T> DeFalha(IReadOnlyList<Erro> erros) => new(false, default, erros);

    /// <summary>Converte um valor cru em resultado de sucesso.</summary>
    public static implicit operator Result<T>(T valor) => DeSucesso(valor);

    /// <summary>Converte um erro cru em resultado de falha.</summary>
    public static implicit operator Result<T>(Erro erro) => DeFalha([erro]);
}

/// <summary>
/// Composição de resultados, para encadear passos sem uma escada de <c>if (r.Falhou) return r;</c>.
/// </summary>
public static class ResultExtensions
{
    /// <summary>Transforma o valor de um resultado bem-sucedido, propagando a falha intacta.</summary>
    public static Result<TDestino> Map<TOrigem, TDestino>(this Result<TOrigem> resultado, Func<TOrigem, TDestino> projecao) =>
        resultado.Sucesso ? Result.Ok(projecao(resultado.Valor)) : Result.Falha<TDestino>(resultado.Erros);

    /// <summary>Encadeia outra operação que também pode falhar.</summary>
    public static Result<TDestino> Bind<TOrigem, TDestino>(this Result<TOrigem> resultado, Func<TOrigem, Result<TDestino>> proximo) =>
        resultado.Sucesso ? proximo(resultado.Valor) : Result.Falha<TDestino>(resultado.Erros);

    /// <summary>Encadeia uma operação assíncrona que também pode falhar.</summary>
    public static async Task<Result<TDestino>> Bind<TOrigem, TDestino>(
        this Result<TOrigem> resultado,
        Func<TOrigem, Task<Result<TDestino>>> proximo
    ) => resultado.Sucesso ? await proximo(resultado.Valor) : Result.Falha<TDestino>(resultado.Erros);

    /// <summary>Executa um efeito colateral no caminho feliz e devolve o resultado inalterado.</summary>
    public static async Task<Result<T>> Tap<T>(this Result<T> resultado, Func<T, Task> efeito)
    {
        if (resultado.Sucesso)
            await efeito(resultado.Valor);

        return resultado;
    }
}
