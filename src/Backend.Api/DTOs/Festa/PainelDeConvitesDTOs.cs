namespace Backend.Api.DTOs.Festa;

/// <summary>Corpo da capacidade do local de um evento.</summary>
/// <param name="Capacidade">Lugares; nulo é "não sei".</param>
public sealed record CapacidadeRequestDTO(int? Capacidade);

/// <summary>Um formando com convite de pacote preso por parcela em atraso (Sprint 47, D24).</summary>
/// <param name="VinculoId">Vínculo — o que a liberação recebe.</param>
/// <param name="Nome">Nome civil, ou o da conta.</param>
/// <param name="Convites">Quantos convites dele estão presos neste evento.</param>
public sealed record FormandoComConvitePresoDTO(Guid VinculoId, string Nome, int Convites);

/// <summary>O painel de convites de um evento: a conta de lugares sobre os pacotes e quem está preso.</summary>
/// <param name="Evento">A festa ou a colação.</param>
/// <param name="Capacidade">Lugares; nulo sem capacidade informada.</param>
/// <param name="FormandosAtivos">Formandos de hoje.</param>
/// <param name="Beneficios">Convites que os pacotes das cestas concedem neste evento.</param>
/// <param name="Extras">Convites comprados — pedido de convite extra e loja.</param>
/// <param name="Cortesias">Cortesias válidas do evento.</param>
/// <param name="Lugares">Benefícios + extras + cortesias.</param>
/// <param name="Excedente">Quanto passa da capacidade — aviso, não bloqueio; zero se cabe.</param>
/// <param name="Emitidos">Convites de pacote válidos; menos que os benefícios enquanto o evento não tem data, hora e local.</param>
/// <param name="Nomeados">Com nome e documento.</param>
/// <param name="SemNome">Ainda sem titular.</param>
/// <param name="Presos">Formandos com convite preso por atraso.</param>
public sealed record PainelDeConvitesDTO(
    EventoDoConviteDTO Evento,
    int? Capacidade,
    int FormandosAtivos,
    int Beneficios,
    int Extras,
    int Cortesias,
    int Lugares,
    int Excedente,
    int Emitidos,
    int Nomeados,
    int SemNome,
    IReadOnlyList<FormandoComConvitePresoDTO> Presos
);

/// <summary>Quantos convites a liberação soltou.</summary>
/// <param name="Liberados">Convites que voltaram a valer agora; zero se já estavam livres.</param>
public sealed record LiberacaoDeConvitesDTO(int Liberados);
