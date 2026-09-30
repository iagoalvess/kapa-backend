using Backend.Business.Agenda.Models;

namespace Backend.Api.DTOs.Agenda;

/// <summary>Corpo do cadastro de um evento da agenda.</summary>
/// <param name="Titulo">O que é ("Prova da beca").</param>
/// <param name="Tipo">Colação, festa, reunião, prazo ou outro.</param>
/// <param name="Situacao">Ausente, o evento nasce <c>AConfirmar</c>.</param>
/// <param name="Data">O dia (<c>yyyy-MM-dd</c>), no fuso da turma.</param>
/// <param name="Hora">A hora (<c>HH:mm</c>); ausente é evento de dia inteiro.</param>
/// <param name="Local">Onde é.</param>
/// <param name="Descricao">O que mais a turma precisa saber.</param>
public sealed record EventoRequestDTO(
    string? Titulo,
    TipoDeEvento Tipo,
    SituacaoDoEvento? Situacao,
    DateOnly Data,
    TimeOnly? Hora,
    string? Local,
    string? Descricao
);

/// <summary>
/// O que a Página Inicial mostra da agenda.
/// </summary>
/// <remarks>
/// Cancelado não entra na lista: na tela da agenda ele continua visível, com o selo, mas aqui o
/// espaço é do que vai acontecer.
/// </remarks>
/// <param name="Proximos">Até três datas, da mais perto para a mais longe.</param>
public sealed record ResumoDaAgendaDTO(IEnumerable<EventoDTO> Proximos);

/// <summary>
/// Um evento da agenda, como a turma o vê.
/// </summary>
/// <remarks>
/// <paramref name="Data"/> e <paramref name="Hora"/> não são instantes: são o dia e a hora do
/// calendário da turma, e viajam sem fuso justamente para chegarem iguais em qualquer relógio.
/// </remarks>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">O que é.</param>
/// <param name="Tipo">Colação, festa, reunião, prazo ou outro.</param>
/// <param name="Situacao">Confirmado, a confirmar ou cancelado.</param>
/// <param name="Data">O dia (<c>yyyy-MM-dd</c>).</param>
/// <param name="Hora">A hora (<c>HH:mm:ss</c>); nula é dia inteiro.</param>
/// <param name="Local">Onde é.</param>
/// <param name="Descricao">O que mais a turma precisa saber.</param>
public sealed record EventoDTO(
    Guid Id,
    string Titulo,
    TipoDeEvento Tipo,
    SituacaoDoEvento Situacao,
    DateOnly Data,
    TimeOnly? Hora,
    string? Local,
    string? Descricao
);
