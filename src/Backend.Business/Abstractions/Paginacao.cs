namespace Backend.Business.Abstractions;

/// <summary>
/// Parâmetros de paginação recebidos da borda.
/// </summary>
/// <remarks>
/// Sempre chame <see cref="Normalizar"/> antes de usar: a borda aceita qualquer inteiro, e
/// <c>tamanho=100000</c> vindo de fora não pode virar consulta.
/// </remarks>
public sealed record PaginacaoRequest
{
    /// <summary>Teto de itens por página aceito pela API.</summary>
    public const int TamanhoMaximo = 100;

    /// <summary>Página desejada, começando em 1.</summary>
    public int Pagina { get; init; } = 1;

    /// <summary>Itens por página.</summary>
    public int Tamanho { get; init; } = 20;

    /// <summary>Devolve uma cópia com página e tamanho dentro dos limites aceitos.</summary>
    public PaginacaoRequest Normalizar() => this with { Pagina = Math.Max(1, Pagina), Tamanho = Math.Clamp(Tamanho, 1, TamanhoMaximo) };

    /// <summary>Quantidade de registros a pular. Use sobre uma instância já normalizada.</summary>
    public int Pular => (Math.Max(1, Pagina) - 1) * Math.Clamp(Tamanho, 1, TamanhoMaximo);
}

/// <summary>
/// Uma página de resultados com os metadados necessários para navegar.
/// </summary>
/// <typeparam name="T">Tipo dos itens.</typeparam>
/// <param name="Itens">Itens da página atual.</param>
/// <param name="Pagina">Número da página atual, começando em 1.</param>
/// <param name="Tamanho">Itens por página solicitados.</param>
/// <param name="Total">Total de registros que atendem ao filtro, ignorando a paginação.</param>
public sealed record PaginaDe<T>(IReadOnlyList<T> Itens, int Pagina, int Tamanho, long Total)
{
    /// <summary>Quantidade total de páginas.</summary>
    public int TotalPaginas => Tamanho <= 0 ? 0 : (int)Math.Ceiling(Total / (double)Tamanho);

    /// <summary>Indica se existe página seguinte.</summary>
    public bool TemProxima => Pagina < TotalPaginas;

    /// <summary>Página vazia, para consultas sem resultado.</summary>
    public static PaginaDe<T> Vazia(PaginacaoRequest paginacao) => new([], paginacao.Pagina, paginacao.Tamanho, 0);
}
