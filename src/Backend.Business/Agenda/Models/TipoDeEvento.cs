namespace Backend.Business.Agenda.Models;

/// <summary>
/// Que tipo de data é esta.
/// </summary>
/// <remarks>
/// Existe por dois motivos concretos, e não por taxonomia: é o que dá cor e ícone ao cartão, e é
/// como o caixa acha a colação sem adivinhar por título (decisão 2).
/// <para>
/// ponytail: cinco valores, e o quinto é "Outro". Categoria livre cadastrável pela turma só quando
/// alguém pedir a sexta — e aí ela vem sem cor, porque cor é código.
/// </para>
/// </remarks>
public enum TipoDeEvento
{
    /// <summary>A colação de grau. No máximo uma por turma.</summary>
    Colacao,

    /// <summary>A festa de formatura. No máximo uma por turma.</summary>
    Festa,

    /// <summary>Reunião da comissão ou assembleia da turma.</summary>
    Reuniao,

    /// <summary>Data limite de alguma coisa: escolher o buffet, tirar a medida da beca.</summary>
    Prazo,

    /// <summary>O que não é nenhum dos anteriores.</summary>
    Outro,
}

/// <summary>Os tipos que existem no máximo uma vez por turma.</summary>
/// <remarks>
/// A lista mora aqui, e não espalhada em <c>is Colacao or Festa</c>, porque três lugares precisam
/// dela: o service (que devolve o 409 com código), o mapeamento (que monta o índice único parcial) e
/// a projeção da formatura, que lê as duas datas da agenda.
/// </remarks>
public static class TiposDeEvento
{
    /// <summary>Colação e festa: a turma tem uma de cada, ou nenhuma.</summary>
    public static readonly IReadOnlyList<TipoDeEvento> Unicos = [TipoDeEvento.Colacao, TipoDeEvento.Festa];

    /// <summary>Se o tipo só pode aparecer uma vez na agenda da turma.</summary>
    /// <param name="tipo">Tipo do evento.</param>
    public static bool EhUnico(TipoDeEvento tipo) => tipo is TipoDeEvento.Colacao or TipoDeEvento.Festa;
}
