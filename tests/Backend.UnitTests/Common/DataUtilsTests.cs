using Backend.Business.Common.Datas;
using Shouldly;

namespace Backend.UnitTests.Common;

/// <summary>
/// Garante a conversão de fuso.
/// </summary>
/// <remarks>
/// O teste mais importante aqui é o primeiro: <c>FusoDeExibicao</c> lança se o sistema não
/// tiver a base de fusos. Em container Alpine isso depende do pacote <c>tzdata</c> e de a
/// globalização invariante estar desligada — por isso o Dockerfile instala e configura os dois.
/// </remarks>
public sealed class DataUtilsTests
{
    [Fact]
    public void O_fuso_de_exibicao_e_resolvido_no_sistema_operacional()
    {
        Should.NotThrow(() => DataUtils.FusoDeExibicao).ShouldNotBeNull();
    }

    [Fact]
    public void Converter_para_exibicao_e_de_volta_devolve_o_instante_original()
    {
        var utc = new DateTime(2026, 3, 15, 18, 30, 0, DateTimeKind.Utc);

        var local = DataUtils.ParaExibicao(utc);

        DataUtils.ParaUtc(local).ShouldBe(utc);
    }

    [Fact]
    public void Sao_Paulo_esta_tres_horas_atras_de_UTC()
    {
        var utc = new DateTime(2026, 3, 15, 18, 0, 0, DateTimeKind.Utc);

        DataUtils.ParaExibicao(utc).Hour.ShouldBe(15);
    }

    [Fact]
    public void O_dia_local_vira_uma_faixa_de_UTC_que_cobre_24_horas()
    {
        var dia = new DateOnly(2026, 3, 15);

        var inicio = DataUtils.InicioDoDiaEmUtc(dia);
        var fim = DataUtils.FimDoDiaEmUtc(dia);

        (fim - inicio).ShouldBe(TimeSpan.FromHours(24));
        inicio.ShouldBeLessThan(fim);
    }
}
