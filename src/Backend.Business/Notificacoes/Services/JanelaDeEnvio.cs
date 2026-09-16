using Backend.Business.Common.Datas;

namespace Backend.Business.Notificacoes.Services;

/// <summary>
/// Quando a régua pode falar: das 9h às 20h, em dias úteis, no horário de Brasília.
/// </summary>
/// <remarks>
/// Decisão 4: cobrança às 3h da manhã é o que faz alguém marcar o remetente como spam — e aí a
/// comissão perde o canal para a turma inteira, não só para essa pessoa.
/// <para>
/// O instante chega em UTC (é o que o banco e o worker têm) e é convertido aqui, uma vez. Comparar
/// a hora de UTC com "9h" erraria por três horas o ano inteiro, e o dia inteiro das 21h à
/// meia-noite.
/// </para>
/// </remarks>
public static class JanelaDeEnvio
{
    /// <summary>Primeira hora em que a régua fala.</summary>
    public const int HoraDeAbertura = 9;

    /// <summary>Primeira hora em que ela já não fala mais — 20h é o fim, e às 20h não sai nada.</summary>
    public const int HoraDeFechamento = 20;

    /// <summary>Se o instante cai dentro da janela.</summary>
    /// <param name="agoraUtc">Momento, em UTC.</param>
    public static bool Aberta(DateTime agoraUtc)
    {
        var local = DataUtils.ParaExibicao(agoraUtc);

        return Util(DateOnly.FromDateTime(local)) && local.Hour is >= HoraDeAbertura and < HoraDeFechamento;
    }

    /// <summary>O dia de hoje em Brasília — o dia do calendário de quem paga e de quem cobra.</summary>
    /// <param name="agoraUtc">Momento, em UTC.</param>
    public static DateOnly Hoje(DateTime agoraUtc) => DateOnly.FromDateTime(DataUtils.ParaExibicao(agoraUtc));

    /// <summary>Se o dia é útil — sábado e domingo não são.</summary>
    /// <param name="dia">Dia em Brasília.</param>
    public static bool Util(DateOnly dia) => dia.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>
    /// Os dias que esta rodada responde: hoje, mais os não úteis logo antes dele.
    /// </summary>
    /// <remarks>
    /// Sem isto, o degrau que cai num sábado nunca dispara, e a turma perde dois sétimos das
    /// cobranças sem que ninguém perceba. Na segunda-feira a régua cobre sábado, domingo e segunda —
    /// e a idempotência por <c>(parcela, regra, dia)</c> continua valendo, porque o dia de referência
    /// gravado é sempre hoje.
    /// </remarks>
    /// <param name="hoje">Dia em Brasília.</param>
    public static IReadOnlyList<DateOnly> DiasRepresados(DateOnly hoje)
    {
        List<DateOnly> dias = [hoje];

        for (var anterior = hoje.AddDays(-1); !Util(anterior); anterior = anterior.AddDays(-1))
            dias.Add(anterior);

        return dias;
    }
}
