using Backend.Business.Busca.Models;

namespace Backend.Api.DTOs.Busca;

/// <summary>Um acerto da busca do topo.</summary>
/// <param name="Tipo">De onde saiu — é o que decide para onde o clique leva.</param>
/// <param name="Id">O que abrir.</param>
/// <param name="Titulo">A linha que a pessoa reconhece.</param>
/// <param name="Detalhe">A segunda linha, quando distingue dois acertos parecidos. Nulo quando o título basta.</param>
public sealed record ResultadoDaBuscaDTO(TipoDeResultado Tipo, Guid Id, string Titulo, string? Detalhe);

/// <summary>O que a busca achou, por grupo, já recortado pelo que quem perguntou pode ver.</summary>
/// <param name="Membros">Só para a Gestão.</param>
/// <param name="Despesas">Para todo membro.</param>
/// <param name="Fornecedores">Só para a Tesouraria.</param>
/// <param name="Avisos">Do mural; o interno só para a Gestão.</param>
/// <param name="Documentos">Do acervo; o interno só para a Gestão.</param>
public sealed record BuscaNaTurmaDTO(
    IReadOnlyList<ResultadoDaBuscaDTO> Membros,
    IReadOnlyList<ResultadoDaBuscaDTO> Despesas,
    IReadOnlyList<ResultadoDaBuscaDTO> Fornecedores,
    IReadOnlyList<ResultadoDaBuscaDTO> Avisos,
    IReadOnlyList<ResultadoDaBuscaDTO> Documentos
);
