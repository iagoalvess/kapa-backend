using Backend.Business.Enquetes.Models;

namespace Backend.Api.DTOs.Enquetes;

/// <summary>Cadastro da pergunta; limites e prazo são validados no domínio.</summary>
public sealed record EnqueteRequestDTO(
    string? Pergunta,
    string? Descricao,
    TipoDeEnquete Tipo,
    int? MaximoDeEscolhas,
    int QuorumMinimoPercentual,
    DateTime EncerraEm,
    IReadOnlyList<string>? Opcoes
);

/// <summary>Somente as alternativas do próprio titular; nunca recebe vínculo ou usuário do cliente.</summary>
public sealed record RespostaDaEnqueteRequestDTO(IReadOnlyList<Guid>? Opcoes);

/// <summary>Consulta como a lista e o Início a apresentam.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Pergunta">A pergunta.</param>
/// <param name="Descricao">Contexto curto, se houver.</param>
/// <param name="Tipo">Escolha única ou múltipla.</param>
/// <param name="MaximoDeEscolhas">Teto de escolhas na múltipla; nulo na única.</param>
/// <param name="QuorumMinimoPercentual">Quórum para a consulta valer, em percentual inteiro.</param>
/// <param name="TotalAptos">Quantos podem responder.</param>
/// <param name="TotalRespostas">Quantos já responderam.</param>
/// <param name="QuorumAtingido">Se o total de respostas alcançou o quórum.</param>
/// <param name="Situacao"><c>Aberta</c>, <c>Encerrada</c> ou <c>Cancelada</c>.</param>
/// <param name="CriadoEm">Publicação, em UTC.</param>
/// <param name="EncerraEm">Prazo, em UTC.</param>
/// <param name="EncerradaEm">Encerramento antecipado, se houve.</param>
/// <param name="CanceladaEm">Cancelamento, se houve.</param>
/// <param name="OpcoesTravadas">Se as alternativas já foram usadas e não mudam mais.</param>
/// <param name="MinhaResposta">As escolhas do próprio titular, se já respondeu.</param>
/// <param name="PodeResponder">Se o botão de responder aparece para quem lê.</param>
/// <param name="MotivoDoBloqueio">Por que não pode responder; nulo quando pode.</param>
public sealed record EnqueteResumoDTO(
    Guid Id,
    string Pergunta,
    string? Descricao,
    TipoDeEnquete Tipo,
    int? MaximoDeEscolhas,
    int QuorumMinimoPercentual,
    int TotalAptos,
    int TotalRespostas,
    bool QuorumAtingido,
    string Situacao,
    DateTime CriadoEm,
    DateTime EncerraEm,
    DateTime? EncerradaEm,
    DateTime? CanceladaEm,
    bool OpcoesTravadas,
    IReadOnlyList<Guid>? MinhaResposta,
    bool PodeResponder,
    string? MotivoDoBloqueio
);

/// <summary>Resultado agregado de uma alternativa, sem identidade de eleitor.</summary>
/// <param name="Id">Alternativa.</param>
/// <param name="Texto">O que a alternativa diz.</param>
/// <param name="Ordem">Posição na tela.</param>
/// <param name="Respostas">Quantos escolheram.</param>
/// <param name="Percentual">Fração do total, de 0 a 100.</param>
public sealed record OpcaoComResultadoDTO(Guid Id, string Texto, int Ordem, int Respostas, decimal Percentual);

/// <summary>Identidade de quem participou; nunca acompanha as escolhas.</summary>
/// <param name="Nome">Nome de quem respondeu.</param>
public sealed record ParticipanteDaEnqueteDTO(string Nome);

/// <summary>Detalhe da consulta com resultado ao vivo e participação visível apenas à Gestão.</summary>
/// <param name="Enquete">O resumo da consulta.</param>
/// <param name="Opcoes">As alternativas com o resultado agregado.</param>
/// <param name="Participantes">Quem já respondeu; nulo fora da Gestão.</param>
public sealed record EnqueteDetalheDTO(
    EnqueteResumoDTO Enquete,
    IReadOnlyList<OpcaoComResultadoDTO> Opcoes,
    IReadOnlyList<ParticipanteDaEnqueteDTO>? Participantes
);
