namespace Backend.Business.Festa.Models;

/// <summary>A cota como a Gestão a informa (Sprint 30, decisão 1).</summary>
/// <param name="CotaPorFormando">Convites por formando ativo; nulo tira a cota de um evento que ainda não a abriu.</param>
/// <param name="Capacidade">Lugares do auditório; nulo é "não sei".</param>
public sealed record DadosDaCota(int? CotaPorFormando, int? Capacidade);

/// <summary>O que o banco conta para o painel da cota.</summary>
/// <param name="FormandosAtivos">Vínculos ativos da turma — cada um recebe a cota.</param>
/// <param name="Emitidos">Convites de cota válidos.</param>
/// <param name="Nomeados">Desses, os que já têm nome e documento.</param>
/// <param name="Cortesias">Cortesias válidas do evento — ocupam cadeira (P3).</param>
public sealed record ContagemDaCota(int FormandosAtivos, int Emitidos, int Nomeados, int Cortesias);

/// <summary>
/// O painel da cota na agenda: o número, a capacidade e a conta aberta.
/// </summary>
/// <remarks>
/// <see cref="Excedente"/> é aviso, não bloqueio (decisão 3): o número de formandos muda depois do
/// cadastro, e um bloqueio que depende dele travaria a comissão num dia em que ela não fez nada.
/// </remarks>
/// <param name="Evento">O evento da cota.</param>
/// <param name="CotaPorFormando">Convites por formando; nulo sem cota.</param>
/// <param name="Capacidade">Lugares; nulo sem capacidade informada.</param>
/// <param name="AbertaEm">Quando a cota foi aberta pela primeira vez; nulo se nunca.</param>
/// <param name="FormandosAtivos">Formandos de hoje.</param>
/// <param name="Cortesias">Cortesias válidas do evento.</param>
/// <param name="Lugares">Cota × formandos ativos + cortesias — as cadeiras que a turma vai ocupar.</param>
/// <param name="Excedente">Quanto <see cref="Lugares"/> passa da capacidade; zero se cabe ou se não há capacidade.</param>
/// <param name="Emitidos">Convites de cota válidos.</param>
/// <param name="Nomeados">Desses, com nome e documento.</param>
/// <param name="SemNome">Desses, ainda sem titular — não entram na lista da portaria (P1).</param>
public sealed record PainelDaCota(
    EventoDoConvite Evento,
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
