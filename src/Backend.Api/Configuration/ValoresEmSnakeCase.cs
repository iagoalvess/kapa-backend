using System.Text;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Backend.Api.Configuration;

/// <summary>
/// Faz a query string e o multipart falarem snake_case, como o corpo JSON.
/// </summary>
/// <remarks>
/// <c>JsonNamingPolicy</c> vale só para o corpo JSON. Query e formulário passam pelo model binder do
/// MVC, que casa o nome do parâmetro ignorando caixa mas não underscore: sem isto,
/// <c>?ordenar_por=nome</c> seria ignorado em silêncio e a listagem voltaria na ordem padrão, sem
/// erro nenhum para denunciar.
/// <para>
/// A alternativa era <c>[FromQuery(Name = "…")]</c> em cada campo composto — mas os filtros são
/// records do <c>Business</c>, e anotá-los traria o MVC para dentro da camada que não referencia
/// ninguém. Aqui a tradução fica onde o transporte mora.
/// </para>
/// <para>
/// A conversão é de mão única e só de nome: o binder pergunta por <c>ValorEmCentavos</c> e nós
/// procuramos <c>valor_em_centavos</c>. Nome que já venha sem maiúscula atravessa intacto, então
/// <c>busca</c>, <c>pagina</c> e <c>status</c> continuam valendo sem custo.
/// </para>
/// </remarks>
public sealed class ValoresEmSnakeCase : IValueProviderFactory
{
    /// <inheritdoc />
    public Task CreateValueProviderAsync(ValueProviderFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var originais = context.ValueProviders.ToList();

        context.ValueProviders.Clear();

        foreach (var provedor in originais)
            context.ValueProviders.Add(new ProvedorEmSnakeCase(provedor));

        return Task.CompletedTask;
    }
}

/// <summary>
/// Envolve um provedor de valores traduzindo o nome pedido para snake_case.
/// </summary>
/// <remarks>
/// Implementa as três interfaces do original, e não só <see cref="IValueProvider"/>: é
/// <see cref="IBindingSourceValueProvider.Filter"/> que separa o que veio da query do que veio do
/// formulário, e um envoltório sem ela é descartado inteiro na filtragem — a requisição chega ao
/// controller com todo parâmetro no valor padrão, sem erro nenhum indicando o que houve.
/// </remarks>
/// <param name="interno">Provedor original — query, formulário ou rota.</param>
internal sealed class ProvedorEmSnakeCase(IValueProvider interno) : IValueProvider, IBindingSourceValueProvider, IEnumerableValueProvider
{
    /// <inheritdoc />
    /// <remarks>O nome como o binder o pede vem primeiro; o snake_case é a segunda tentativa.</remarks>
    public bool ContainsPrefix(string prefix) => interno.ContainsPrefix(prefix) || interno.ContainsPrefix(Converter(prefix));

    /// <inheritdoc />
    /// <remarks>
    /// Tenta o nome original antes do convertido, e não o contrário: o mesmo provedor serve à rota,
    /// e ali o nome é o do template (<c>{usuarioId:guid}</c>), escrito por nós e não pelo cliente.
    /// Converter primeiro faria toda rota com parâmetro composto deixar de casar — e o endpoint
    /// responderia "não encontrado" para um identificador que existe.
    /// </remarks>
    public ValueProviderResult GetValue(string key)
    {
        var original = interno.GetValue(key);

        return original.Length > 0 ? original : interno.GetValue(Converter(key));
    }

    /// <inheritdoc />
    /// <remarks>Provedor que não filtra por fonte atravessa como está — é o comportamento do original.</remarks>
    public IValueProvider? Filter(BindingSource bindingSource)
    {
        if (interno is not IBindingSourceValueProvider filtravel)
            return this;

        return filtravel.Filter(bindingSource) is { } filtrado ? new ProvedorEmSnakeCase(filtrado) : null;
    }

    /// <inheritdoc />
    public IDictionary<string, string> GetKeysFromPrefix(string prefix) =>
        interno is IEnumerableValueProvider enumeravel ? enumeravel.GetKeysFromPrefix(Converter(prefix)) : new Dictionary<string, string>(0);

    /// <summary>
    /// <c>Itens[0].ValorEmCentavos</c> vira <c>itens[0].valor_em_centavos</c>.
    /// </summary>
    /// <remarks>Cada segmento do caminho é convertido por conta própria; o índice atravessa intacto.</remarks>
    /// <param name="nome">Nome pedido pelo binder.</param>
    private static string Converter(string nome) =>
        string.IsNullOrEmpty(nome) || !nome.Any(char.IsUpper) ? nome : string.Join('.', nome.Split('.').Select(Segmento));

    private static string Segmento(string segmento)
    {
        var colchete = segmento.IndexOf('[', StringComparison.Ordinal);
        var nome = colchete < 0 ? segmento : segmento[..colchete];
        var sufixo = colchete < 0 ? string.Empty : segmento[colchete..];

        var texto = new StringBuilder(nome.Length + 8);

        for (var i = 0; i < nome.Length; i++)
        {
            if (char.IsUpper(nome[i]) && i > 0)
                texto.Append('_');

            texto.Append(char.ToLowerInvariant(nome[i]));
        }

        return texto.Append(sufixo).ToString();
    }
}
