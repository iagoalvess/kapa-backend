namespace Backend.Business.Busca.Models;

/// <summary>
/// De onde saiu um acerto da busca — é o que a tela agrupa e o que decide para onde o clique leva.
/// </summary>
/// <remarks>
/// A rota fica no frontend, e não aqui: caminho de tela é assunto de quem desenha a tela, e uma
/// API que devolve <c>/financeiro/despesas/{id}</c> passa a quebrar quando alguém renomeia a rota.
/// </remarks>
public enum TipoDeResultado
{
    /// <summary>Membro da turma, pelo nome da conta, pelo nome completo ou pelo e-mail.</summary>
    Membro,

    /// <summary>Despesa, pela descrição ou pelo nome do fornecedor.</summary>
    Despesa,

    /// <summary>Fornecedor, pelo nome.</summary>
    Fornecedor,

    /// <summary>Aviso do mural, pelo título ou pelo texto.</summary>
    Aviso,

    /// <summary>Documento do acervo, pelo título.</summary>
    Documento,
}

/// <summary>
/// Um acerto da busca.
/// </summary>
/// <param name="Tipo">De onde saiu.</param>
/// <param name="Id">O que abrir — o id do membro, da despesa, do aviso.</param>
/// <param name="Titulo">A linha que a pessoa reconhece: o nome, a descrição, o título.</param>
/// <param name="Detalhe">
/// A segunda linha, quando ela distingue dois acertos parecidos: o papel do membro, o valor da
/// despesa, a categoria do documento. Nulo quando o título já basta.
/// </param>
public sealed record ResultadoDaBusca(TipoDeResultado Tipo, Guid Id, string Titulo, string? Detalhe);

/// <summary>
/// O que a busca do topo enxerga, já recortado pelo que quem pergunta pode ver.
/// </summary>
/// <remarks>
/// Recorte por papel dentro da consulta, e não filtro depois: a Comissão não vê fornecedor, o
/// formando não vê a lista de membros nem o aviso interno, e devolver para filtrar na tela seria
/// mandar pelo fio justamente o que a pessoa não pode ler.
/// </remarks>
/// <param name="Membros">Só para a Gestão — a lista de membros é dela.</param>
/// <param name="Despesas">Para todo membro: quem paga três anos de parcela vê no que a turma gastou.</param>
/// <param name="Fornecedores">Só para a Tesouraria, como o item de menu.</param>
/// <param name="Avisos">Do mural; o interno só aparece para a Gestão.</param>
/// <param name="Documentos">Do acervo; o interno só aparece para a Gestão.</param>
public sealed record BuscaNaTurma(
    IReadOnlyList<ResultadoDaBusca> Membros,
    IReadOnlyList<ResultadoDaBusca> Despesas,
    IReadOnlyList<ResultadoDaBusca> Fornecedores,
    IReadOnlyList<ResultadoDaBusca> Avisos,
    IReadOnlyList<ResultadoDaBusca> Documentos
)
{
    /// <summary>Nada encontrado em grupo nenhum.</summary>
    public bool Vazia => Membros.Count + Despesas.Count + Fornecedores.Count + Avisos.Count + Documentos.Count == 0;

    /// <summary>O resultado de quem digitou pouco — dois caracteres não recortam nada.</summary>
    public static BuscaNaTurma Nada => new([], [], [], [], []);
}

/// <summary>
/// Quem está perguntando, do ponto de vista da busca.
/// </summary>
/// <param name="FormaturaId">Turma da sessão.</param>
/// <param name="Papel">Papel ativo, como o token o traz. Nulo em sessão sem turma.</param>
public sealed record QuemBusca(Guid FormaturaId, string? Papel);
