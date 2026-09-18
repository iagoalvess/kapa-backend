namespace Backend.Api.DTOs.Auditoria;

/// <summary>Uma linha da trilha de auditoria.</summary>
/// <param name="Id">Identificador do evento.</param>
/// <param name="Nome">Nome estável do evento, <c>recurso.acao</c>.</param>
/// <param name="OcorridoEm">Quando aconteceu, em UTC.</param>
/// <param name="AutorUsuarioId">Quem fez. Nulo em ação de sistema.</param>
/// <param name="Autor">Nome de quem fez. Nulo em ação de sistema.</param>
/// <param name="Dados">Corpo do evento em JSON, com o antes e o depois quando houver.</param>
/// <param name="Pessoas">Nome de cada pessoa citada no corpo, por id. Vazio quando não cita ninguém.</param>
public sealed record LinhaDeAuditoriaDTO(
    Guid Id,
    string Nome,
    DateTime OcorridoEm,
    Guid? AutorUsuarioId,
    string? Autor,
    string? Dados,
    IReadOnlyDictionary<string, string> Pessoas
);

/// <summary>O recorte pedido pela tela, na query string.</summary>
/// <param name="De">Primeiro dia do período, inclusive.</param>
/// <param name="Ate">Último dia do período, inclusive.</param>
/// <param name="Autor">Só o que esta pessoa fez.</param>
/// <param name="Nome">Só este tipo de evento.</param>
/// <param name="Busca">Nome de pessoa — de quem fez ou de quem sofreu — ou texto do corpo do evento.</param>
public sealed record FiltroDeAuditoriaDTO(DateOnly? De, DateOnly? Ate, Guid? Autor, string? Nome, string? Busca);

/// <summary>Um autor que aparece na trilha da turma.</summary>
/// <param name="UsuarioId">Autor.</param>
/// <param name="Nome">Nome de exibição.</param>
public sealed record AutorDeAuditoriaDTO(Guid UsuarioId, string Nome);

/// <summary>O que os seletores da tela de Auditoria oferecem.</summary>
/// <param name="Autores">Quem já fez alguma coisa auditável nesta turma.</param>
/// <param name="Nomes">Os nomes de evento que a turma tem registrados.</param>
public sealed record OpcoesDeAuditoriaDTO(IReadOnlyList<AutorDeAuditoriaDTO> Autores, IReadOnlyList<string> Nomes);

/// <summary>Um item contado da trilha.</summary>
/// <param name="Rotulo">Nome da pessoa ou nome estável do evento.</param>
/// <param name="Quantidade">Quantas linhas da trilha são dele.</param>
public sealed record ContagemDaAuditoriaDTO(string Rotulo, int Quantidade);

/// <summary>Os números do topo da tela de Auditoria, da turma inteira e sem filtro.</summary>
/// <param name="Total">Quantas ações a turma tem registradas.</param>
/// <param name="NosUltimosTrintaDias">Quantas aconteceram nos últimos trinta dias.</param>
/// <param name="UltimaEm">Quando foi a última, em UTC. Nulo: trilha vazia.</param>
/// <param name="UltimoNome">O nome estável do evento mais recente.</param>
/// <param name="QuemMaisFez">Quem mais aparece como autor.</param>
/// <param name="AcaoMaisComum">O nome de evento mais frequente.</param>
public sealed record ResumoDaAuditoriaDTO(
    long Total,
    int NosUltimosTrintaDias,
    DateTime? UltimaEm,
    string? UltimoNome,
    ContagemDaAuditoriaDTO? QuemMaisFez,
    ContagemDaAuditoriaDTO? AcaoMaisComum
);
