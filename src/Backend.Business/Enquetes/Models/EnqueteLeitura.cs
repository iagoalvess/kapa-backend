namespace Backend.Business.Enquetes.Models;

/// <summary>Cadastro informado pela comissão; as alternativas são textos em ordem.</summary>
public sealed record DadosDaEnquete(
    string Pergunta,
    string? Descricao,
    TipoDeEnquete Tipo,
    int? MaximoDeEscolhas,
    int QuorumMinimoPercentual,
    DateTime EncerraEm,
    IReadOnlyList<string> Opcoes
);

/// <summary>Vínculo e requisitos de votação, lidos do banco para o próprio solicitante.</summary>
public sealed record MembroParaEnquete(Guid VinculoId, bool EmailConfirmado, bool Aderiu, bool Gestao);

/// <summary>Dados internos de leitura sem escolhas de terceiros.</summary>
public sealed record EnqueteComContagem(Enquete Enquete, int Respondentes, IReadOnlyList<Guid>? MinhaResposta);

/// <summary>Consulta como a lista e a home a apresentam.</summary>
public sealed record EnqueteResumo(
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
public sealed record OpcaoComResultado(Guid Id, string Texto, int Ordem, int Respostas, decimal Percentual);

/// <summary>Identidade de quem participou; nunca acompanha as escolhas.</summary>
public sealed record ParticipanteDaEnquete(string Nome);

/// <summary>Detalhe da consulta com resultado ao vivo e participação visível apenas à Gestão.</summary>
public sealed record EnqueteDetalhe(
    EnqueteResumo Enquete,
    IReadOnlyList<OpcaoComResultado> Opcoes,
    IReadOnlyList<ParticipanteDaEnquete>? Participantes
);

/// <summary>Uma resposta do próprio titular para seu pacote de dados.</summary>
public sealed record MinhaRespostaExportada(Guid EnqueteId, string Pergunta, DateTime RespondidaEm, IReadOnlyList<string> Escolhas);
