using Backend.Business.Abstractions;

namespace Backend.Api.DTOs.Comum;

/// <summary>
/// Parâmetros de paginação recebidos na query string.
/// </summary>
/// <param name="Pagina">Página desejada, começando em 1.</param>
/// <param name="Tamanho">Itens por página. O teto é aplicado no servidor.</param>
public sealed record PaginacaoRequestDTO(int Pagina = 1, int Tamanho = 20)
{
    /// <summary>Converte para o modelo de paginação da camada de negócio.</summary>
    public PaginacaoRequest ParaModelo() => new() { Pagina = Pagina, Tamanho = Tamanho };
}

/// <summary>
/// Página de resultados devolvida pela API.
/// </summary>
/// <typeparam name="T">Tipo dos itens.</typeparam>
/// <param name="Itens">Itens da página atual.</param>
/// <param name="Pagina">Página atual, começando em 1.</param>
/// <param name="Tamanho">Itens por página.</param>
/// <param name="Total">Total de registros que atendem ao filtro.</param>
/// <param name="TotalPaginas">Quantidade de páginas.</param>
/// <param name="TemProxima">Indica se existe página seguinte.</param>
public sealed record PaginaDTO<T>(IReadOnlyList<T> Itens, int Pagina, int Tamanho, long Total, int TotalPaginas, bool TemProxima);

/// <summary>
/// Conversão de páginas do domínio para o contrato HTTP.
/// </summary>
public static class PaginaDTOExtensions
{
    /// <summary>Converte uma página do domínio, projetando cada item.</summary>
    /// <typeparam name="TOrigem">Tipo do item no domínio.</typeparam>
    /// <typeparam name="TDestino">Tipo do item no contrato.</typeparam>
    /// <param name="pagina">Página produzida pelo service.</param>
    /// <param name="projecao">Conversão item a item.</param>
    public static PaginaDTO<TDestino> ParaDTO<TOrigem, TDestino>(this PaginaDe<TOrigem> pagina, Func<TOrigem, TDestino> projecao) =>
        new([.. pagina.Itens.Select(projecao)], pagina.Pagina, pagina.Tamanho, pagina.Total, pagina.TotalPaginas, pagina.TemProxima);
}
