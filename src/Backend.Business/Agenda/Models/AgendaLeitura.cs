namespace Backend.Business.Agenda.Models;

/// <summary>Um evento, como a comissão o informa.</summary>
/// <param name="Titulo">O que é.</param>
/// <param name="Tipo">Que tipo de data é.</param>
/// <param name="Situacao">Confirmado, a confirmar ou cancelado.</param>
/// <param name="Data">O dia, no fuso da turma.</param>
/// <param name="Hora">A hora, quando importa; nulo é dia inteiro.</param>
/// <param name="Local">Onde é.</param>
/// <param name="Descricao">O que mais a turma precisa saber.</param>
public sealed record DadosDoEvento(
    string Titulo,
    TipoDeEvento Tipo,
    SituacaoDoEvento Situacao,
    DateOnly Data,
    TimeOnly? Hora,
    string? Local,
    string? Descricao
);

/// <summary>
/// O que a Página Inicial mostra da agenda: as próximas datas, e quantas ainda vêm.
/// </summary>
/// <remarks>
/// Endpoint próprio, e não a lista inteira: a home já paga cinco consultas, e o que ela desenha são
/// três linhas. Sem o recorte, o custo dela cresceria com a agenda da turma — e a lista completa
/// seria pedida em toda abertura do app para mostrar as três primeiras.
/// <para>
/// Cancelado fica de fora dos dois números: na agenda ele continua na lista, com o selo, porque lá
/// a turma precisa ver que aquilo foi desmarcado; aqui o espaço é do que vai acontecer.
/// </para>
/// </remarks>
/// <param name="Proximos">As próximas datas, da mais perto para a mais longe.</param>
/// <param name="Total">Quantas datas ainda vêm, contando as que não couberam.</param>
public sealed record ResumoDaAgenda(IReadOnlyList<EventoResumo> Proximos, int Total);

/// <summary>
/// Um evento como a turma o vê.
/// </summary>
/// <remarks>
/// Não há campo derivado aqui, e é de propósito: agrupar por mês, separar o passado e contar o que
/// falta confirmar são decisões de desenho da tela, feitas sobre a lista que já chegou inteira
/// (decisão 10). Somar isso no servidor seria devolver três vezes o mesmo dado.
/// </remarks>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">O que é.</param>
/// <param name="Tipo">Que tipo de data é.</param>
/// <param name="Situacao">Confirmado, a confirmar ou cancelado.</param>
/// <param name="Data">O dia, no fuso da turma.</param>
/// <param name="Hora">A hora; nulo é dia inteiro.</param>
/// <param name="Local">Onde é.</param>
/// <param name="Descricao">O que mais a turma precisa saber.</param>
public sealed record EventoResumo(
    Guid Id,
    string Titulo,
    TipoDeEvento Tipo,
    SituacaoDoEvento Situacao,
    DateOnly Data,
    TimeOnly? Hora,
    string? Local,
    string? Descricao
);
