namespace Backend.Business.Abstractions;

/// <summary>
/// Qual formatura a requisição está enxergando.
/// </summary>
/// <remarks>
/// Fica em <c>Abstractions</c>, e não na Api, porque quem mais precisa dela é o
/// <c>AppDbContext</c> — e <c>Backend.Data</c> não referencia <c>Backend.Api</c>.
/// <para>
/// O valor vem da claim <c>formatura_id</c> do access token, nunca de cabeçalho: cabeçalho é
/// escolhido pelo cliente, e "o cliente diz de qual turma são os dados" é exatamente o buraco
/// que o isolamento existe para fechar.
/// </para>
/// </remarks>
public interface IFormaturaAtual
{
    /// <summary>Formatura selecionada, ou nulo quando não há.</summary>
    Guid? Id { get; }
}

/// <summary>
/// Contexto sem formatura selecionada.
/// </summary>
/// <remarks>
/// É o registro padrão de <c>AddData</c>, para o worker e a CLI do EF Core resolverem o
/// contexto fora de uma requisição HTTP. Com ele, o filtro global não casa com linha nenhuma —
/// quem precisa cruzar formaturas usa <c>IgnoreQueryFilters</c> em método com sufixo
/// <c>DeTodasAsFormaturas</c>.
/// </remarks>
public sealed class SemFormaturaSelecionada : IFormaturaAtual
{
    /// <inheritdoc />
    public Guid? Id => null;
}
