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

/// <summary>
/// A formatura que o worker está processando neste escopo.
/// </summary>
/// <remarks>
/// O worker não tem requisição HTTP e, portanto, não tem claim: com
/// <see cref="SemFormaturaSelecionada"/>, o filtro global não casa com linha nenhuma — que é o
/// padrão correto e seguro. Um job que precisa ler os dados de uma turma (o balancete da Sprint 12)
/// aponta o escopo para ela antes, e o isolamento continua valendo dentro dele.
/// <para>
/// <b>Um escopo por formatura, sempre.</b> Reaproveitar o escopo entre duas turmas apontaria o
/// mesmo <c>DbContext</c> — com o cache de entidades já povoado — para outra turma. Quem abre o
/// escopo é o job; esta classe só guarda o valor.
/// </para>
/// </remarks>
public sealed class FormaturaDoProcessamento : IFormaturaAtual
{
    /// <inheritdoc />
    public Guid? Id { get; private set; }

    /// <summary>Aponta o escopo para uma formatura. Chamado uma vez, antes da primeira consulta.</summary>
    /// <param name="formaturaId">Formatura a processar.</param>
    /// <exception cref="InvalidOperationException">Se o escopo já estiver apontado para outra formatura.</exception>
    public void Apontar(Guid formaturaId)
    {
        if (Id is { } atual && atual != formaturaId)
            throw new InvalidOperationException("O escopo já está apontado para outra formatura. Abra um escopo novo.");

        Id = formaturaId;
    }
}
