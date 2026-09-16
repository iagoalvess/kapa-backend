using System.Linq.Expressions;

namespace Backend.Data.Repositories;

/// <summary>
/// A ordenação que a tela pede, aplicada à consulta.
/// </summary>
/// <remarks>
/// A coluna vem da query string e nunca vira texto dentro da consulta: cada listagem a traduz num
/// <c>switch</c> sobre as colunas que aceita — o que não estiver no <c>switch</c> cai no <c>_</c>, a
/// ordenação padrão dela. É lista de permissão pela forma, e não por lembrança de quem escreve.
/// <para>
/// O desempate por id continua obrigatório em toda listagem paginada: sem ele, duas linhas com o
/// mesmo valor na coluna ordenada trocam de página entre uma consulta e outra, e um item some.
/// </para>
/// </remarks>
internal static class Ordenacao
{
    /// <summary>Ordena pela chave, crescente ou decrescente.</summary>
    /// <typeparam name="T">A linha da consulta.</typeparam>
    /// <typeparam name="TChave">O tipo da coluna — inferido, e por isso verificado pelo compilador.</typeparam>
    /// <param name="consulta">A consulta já filtrada.</param>
    /// <param name="chave">A coluna.</param>
    /// <param name="descendente">Se é decrescente.</param>
    public static IOrderedQueryable<T> Por<T, TChave>(this IQueryable<T> consulta, Expression<Func<T, TChave>> chave, bool descendente) =>
        descendente ? consulta.OrderByDescending(chave) : consulta.OrderBy(chave);
}
