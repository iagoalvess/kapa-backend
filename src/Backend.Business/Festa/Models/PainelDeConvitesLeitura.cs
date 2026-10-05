namespace Backend.Business.Festa.Models;

/// <summary>A capacidade do local, como a Gestão a informa.</summary>
/// <param name="Capacidade">Lugares do auditório ou do salão; nulo é "não sei".</param>
public sealed record DadosDaCapacidade(int? Capacidade);

/// <summary>O que o banco conta para o painel de convites de um evento.</summary>
/// <param name="FormandosAtivos">Vínculos ativos da turma.</param>
/// <param name="Beneficios">Convites que os pacotes das cestas dos formandos ativos concedem neste evento.</param>
/// <param name="Emitidos">Convites de pacote válidos.</param>
/// <param name="Nomeados">Desses, os que já têm nome e documento.</param>
/// <param name="Extras">Convites válidos comprados — pedido de convite extra e loja pública.</param>
/// <param name="Cortesias">Cortesias válidas do evento — ocupam cadeira (Sprint 30, P3).</param>
public sealed record ContagemDoEvento(int FormandosAtivos, int Beneficios, int Emitidos, int Nomeados, int Extras, int Cortesias);

/// <summary>Um formando com convites de pacote presos por atraso (Sprint 47, D24).</summary>
/// <param name="VinculoId">Vínculo — o que a liberação recebe.</param>
/// <param name="Nome">Nome civil, ou o da conta.</param>
/// <param name="Convites">Quantos convites dele estão presos neste evento.</param>
public sealed record FormandoComConvitePreso(Guid VinculoId, string Nome, int Convites);

/// <summary>
/// O painel de convites do evento na agenda: a conta de lugares e quem está preso por atraso.
/// </summary>
/// <remarks>
/// A conta lê os benefícios das cestas, não um número uniforme (D15). <see cref="Excedente"/> é aviso, não bloqueio
/// (Sprint 30, decisão 3): a cesta de cada formando é contrato, e quem decide trocar de local é a comissão.
/// </remarks>
/// <param name="Evento">O evento.</param>
/// <param name="Capacidade">Lugares; nulo sem capacidade informada.</param>
/// <param name="FormandosAtivos">Formandos de hoje.</param>
/// <param name="Beneficios">Convites que os pacotes concedem neste evento.</param>
/// <param name="Extras">Convites comprados — pedido e loja.</param>
/// <param name="Cortesias">Cortesias válidas.</param>
/// <param name="Lugares">Benefícios + extras + cortesias — as cadeiras que a turma vai ocupar.</param>
/// <param name="Excedente">Quanto <see cref="Lugares"/> passa da capacidade; zero se cabe ou se não há capacidade.</param>
/// <param name="Emitidos">Convites de pacote válidos — menos que os benefícios enquanto o evento não está completo na agenda.</param>
/// <param name="Nomeados">Desses, com nome e documento.</param>
/// <param name="SemNome">Desses, ainda sem titular.</param>
/// <param name="Presos">Quem tem convite preso por parcela em atraso — a comissão libera ou cobra.</param>
public sealed record PainelDeConvites(
    EventoDoConvite Evento,
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
    IReadOnlyList<FormandoComConvitePreso> Presos
);
