namespace Backend.Business.Agenda.Models;

/// <summary>
/// Em que pé está a data.
/// </summary>
/// <remarks>
/// <b>É digitada</b>, e é a única situação do produto que é (decisão 4). O estado do item da festa
/// (Sprint 17, decisão 2) sai das despesas porque lá havia de onde sair; aqui não há: nada no
/// sistema sabe se o salão confirmou a data — só a comissão sabe.
/// </remarks>
public enum SituacaoDoEvento
{
    /// <summary>Data reservada, ainda não fechada. É como a maioria dos eventos nasce.</summary>
    AConfirmar,

    /// <summary>Está marcado.</summary>
    Confirmado,

    /// <summary>Não vai acontecer. Continua na lista, com o selo — não some.</summary>
    Cancelado,
}
