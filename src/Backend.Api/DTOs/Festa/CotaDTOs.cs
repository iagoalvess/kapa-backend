namespace Backend.Api.DTOs.Festa;

/// <summary>Corpo da cota: convites por formando e lugares do auditório.</summary>
/// <param name="CotaPorFormando">Convites por formando ativo; nulo tira a cota de um evento que ainda não a abriu.</param>
/// <param name="Capacidade">Lugares; nulo é "não sei".</param>
public sealed record CotaRequestDTO(int? CotaPorFormando, int? Capacidade);

/// <summary>O painel da cota de um evento.</summary>
/// <param name="Evento">A festa ou a colação.</param>
/// <param name="CotaPorFormando">Convites por formando; nulo sem cota.</param>
/// <param name="Capacidade">Lugares; nulo sem capacidade informada.</param>
/// <param name="AbertaEm">Quando a cota foi aberta pela primeira vez; nulo se nunca.</param>
/// <param name="FormandosAtivos">Formandos de hoje.</param>
/// <param name="Cortesias">Cortesias válidas do evento.</param>
/// <param name="Lugares">Cota × formandos ativos + cortesias.</param>
/// <param name="Excedente">Quanto passa da capacidade — aviso, não bloqueio; zero se cabe.</param>
/// <param name="Emitidos">Convites de cota válidos.</param>
/// <param name="Nomeados">Com nome e documento.</param>
/// <param name="SemNome">Ainda sem titular.</param>
public sealed record PainelDaCotaDTO(
    EventoDoConviteDTO Evento,
    int? CotaPorFormando,
    int? Capacidade,
    DateTime? AbertaEm,
    int FormandosAtivos,
    int Cortesias,
    int Lugares,
    int Excedente,
    int Emitidos,
    int Nomeados,
    int SemNome
);
