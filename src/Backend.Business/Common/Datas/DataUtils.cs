namespace Backend.Business.Common.Datas;

/// <summary>
/// Conversão entre UTC e o fuso de exibição da aplicação.
/// </summary>
/// <remarks>
/// A regra do projeto: **armazenar sempre em UTC, converter só para exibir.** Nunca use
/// <c>DateTime.Now</c> — em container o fuso do sistema é UTC e o valor sai errado sem avisar.
/// <para>
/// O identificador do fuso muda entre Windows (<c>E. South America Standard Time</c>) e Linux
/// (<c>America/Sao_Paulo</c>); <see cref="FusoDeExibicao"/> resolve os dois. Para outro país,
/// troque <see cref="FusoIana"/> e <see cref="FusoWindows"/>.
/// </para>
/// </remarks>
public static class DataUtils
{
    /// <summary>Identificador IANA do fuso de exibição (Linux, macOS, .NET 6+ no Windows).</summary>
    public const string FusoIana = "America/Sao_Paulo";

    /// <summary>Identificador Windows do mesmo fuso, usado como alternativa.</summary>
    public const string FusoWindows = "E. South America Standard Time";

    private static readonly Lazy<TimeZoneInfo> FusoResolvido = new(Resolver);

    /// <summary>Fuso usado para exibir datas ao usuário final.</summary>
    public static TimeZoneInfo FusoDeExibicao => FusoResolvido.Value;

    /// <summary>Converte um instante UTC para o fuso de exibição.</summary>
    /// <param name="utc">Instante em UTC.</param>
    public static DateTime ParaExibicao(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), FusoDeExibicao);

    /// <summary>Converte uma data local do fuso de exibição para UTC.</summary>
    /// <param name="local">Data como o usuário a informou.</param>
    public static DateTime ParaUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), FusoDeExibicao);

    /// <summary>Primeiro instante (UTC) do dia informado no fuso de exibição.</summary>
    /// <param name="local">Dia desejado, no fuso de exibição.</param>
    public static DateTime InicioDoDiaEmUtc(DateOnly local) => ParaUtc(local.ToDateTime(TimeOnly.MinValue));

    /// <summary>Primeiro instante (UTC) do dia seguinte — use como limite superior exclusivo.</summary>
    /// <param name="local">Dia desejado, no fuso de exibição.</param>
    public static DateTime FimDoDiaEmUtc(DateOnly local) => ParaUtc(local.AddDays(1).ToDateTime(TimeOnly.MinValue));

    private static TimeZoneInfo Resolver()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(FusoIana, out var iana))
            return iana;

        if (TimeZoneInfo.TryFindSystemTimeZoneById(FusoWindows, out var windows))
            return windows;

        throw new TimeZoneNotFoundException($"Nenhum fuso encontrado para '{FusoIana}' nem '{FusoWindows}'.");
    }
}
