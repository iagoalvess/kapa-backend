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
