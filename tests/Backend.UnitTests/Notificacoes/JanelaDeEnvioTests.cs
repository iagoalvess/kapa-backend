using Backend.Business.Notificacoes.Services;
using Shouldly;

namespace Backend.UnitTests.Notificacoes;

/// <summary>
/// A janela de 9h às 20h em dias úteis, conferida a partir de instantes UTC.
/// </summary>
/// <remarks>
/// Critério de aceite: nada é enviado fora da janela, em horário de Brasília, considerando UTC no
/// banco. É o teste que pega o erro de comparar a hora de UTC com "9h" — que passaria três horas
/// deslocado o ano inteiro.
/// </remarks>
public sealed class JanelaDeEnvioTests
{
    /// <summary>Um instante UTC do dia informado, no fuso de Brasília (UTC−3).</summary>
    private static DateTime Brasilia(int dia, int hora) => new DateTime(2026, 9, dia, hora, 0, 0, DateTimeKind.Utc).AddHours(3);

    [Theory]
    [InlineData(15, 9)]
    [InlineData(15, 14)]
    [InlineData(15, 19)]
    public void Dentro_do_horario_comercial_em_dia_util_a_janela_esta_aberta(int dia, int hora) =>
        JanelaDeEnvio.Aberta(Brasilia(dia, hora)).ShouldBeTrue();

    [Theory]
    [InlineData(15, 3)]
    [InlineData(15, 8)]
    [InlineData(15, 20)]
    [InlineData(15, 23)]
    public void Fora_do_horario_comercial_a_janela_esta_fechada(int dia, int hora) => JanelaDeEnvio.Aberta(Brasilia(dia, hora)).ShouldBeFalse();

    /// <summary>12 e 13 de setembro de 2026 são sábado e domingo.</summary>
    [Theory]
    [InlineData(12)]
    [InlineData(13)]
    public void No_fim_de_semana_a_janela_esta_fechada_mesmo_as_14h(int dia) => JanelaDeEnvio.Aberta(Brasilia(dia, 14)).ShouldBeFalse();

    /// <summary>
    /// Às 22h de Brasília, UTC já está no dia seguinte — e o D+3 sairia um dia antes.
    /// </summary>
    [Fact]
    public void O_dia_de_hoje_e_o_de_Brasilia_e_nao_o_de_UTC()
    {
        var utc = new DateTime(2026, 9, 16, 1, 0, 0, DateTimeKind.Utc);

        JanelaDeEnvio.Hoje(utc).ShouldBe(new DateOnly(2026, 9, 15));
    }

    /// <summary>Segunda-feira cobre o sábado e o domingo que a régua não pôde responder.</summary>
    [Fact]
    public void A_segunda_feira_responde_pelos_dias_represados_do_fim_de_semana() =>
        JanelaDeEnvio.DiasRepresados(new DateOnly(2026, 9, 14)).ShouldBe([new(2026, 9, 14), new(2026, 9, 13), new(2026, 9, 12)]);

    [Fact]
    public void Um_dia_util_no_meio_da_semana_responde_so_por_ele() =>
        JanelaDeEnvio.DiasRepresados(new DateOnly(2026, 9, 15)).ShouldBe([new(2026, 9, 15)]);
}
