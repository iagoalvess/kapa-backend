using Backend.Business.Relatorios.Models;
using Shouldly;

namespace Backend.UnitTests.Relatorios;

/// <summary>
/// O período que a borda manda pela metade, ou invertido, ou com cinquenta anos de intervalo.
/// </summary>
public sealed class PeriodoDoRelatorioTests
{
    private static readonly DateOnly Hoje = new(2026, 9, 15);

    [Fact]
    public void Sem_nada_e_o_ano_corrente_ate_hoje()
    {
        // Act
        var periodo = PeriodoDoRelatorio.Normalizar(null, null, Hoje);

        // Assert
        periodo.De.ShouldBe(new DateOnly(2026, 1, 1));
        periodo.Ate.ShouldBe(Hoje);
    }

    [Fact]
    public void So_o_fim_leva_o_comeco_para_o_primeiro_de_janeiro_do_ano_dele()
    {
        // Act
        var periodo = PeriodoDoRelatorio.Normalizar(null, new DateOnly(2025, 6, 30), Hoje);

        // Assert
        periodo.De.ShouldBe(new DateOnly(2025, 1, 1));
        periodo.Ate.ShouldBe(new DateOnly(2025, 6, 30));
    }

    /// <summary>
    /// Data invertida é trocada, não recusada: a pessoa quis o intervalo entre as duas, e um 400 aqui
    /// só a faria digitar de novo na outra ordem.
    /// </summary>
    [Fact]
    public void Periodo_invertido_troca_as_pontas()
    {
        // Act
        var periodo = PeriodoDoRelatorio.Normalizar(new DateOnly(2026, 9, 1), new DateOnly(2026, 3, 1), Hoje);

        // Assert
        periodo.De.ShouldBe(new DateOnly(2026, 3, 1));
        periodo.Ate.ShouldBe(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void Intervalo_maior_que_o_teto_e_cortado_pelo_comeco()
    {
        // Act
        var periodo = PeriodoDoRelatorio.Normalizar(new DateOnly(1990, 1, 1), Hoje, Hoje);

        // Assert
        periodo.Ate.ShouldBe(Hoje);
        periodo.De.ShouldBe(Hoje.AddDays(-PeriodoDoRelatorio.MaximoDeDias));
    }

    /// <summary>O anterior encosta na véspera e tem o mesmo tamanho — é o que faz a variação significar algo.</summary>
    [Fact]
    public void O_periodo_anterior_termina_na_vespera_e_tem_o_mesmo_tamanho()
    {
        // Arrange
        var periodo = new PeriodoDoRelatorio(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        // Act
        var anterior = periodo.Anterior();

        // Assert
        anterior.Ate.ShouldBe(new DateOnly(2026, 8, 31));
        anterior.De.ShouldBe(new DateOnly(2026, 8, 2));
        anterior.Dias.ShouldBe(periodo.Dias);
    }

    /// <summary>Um dia só também tem véspera: o anterior é o dia anterior, e não um intervalo vazio.</summary>
    [Fact]
    public void Um_dia_so_tem_como_anterior_a_vespera()
    {
        // Act
        var anterior = new PeriodoDoRelatorio(Hoje, Hoje).Anterior();

        // Assert
        anterior.De.ShouldBe(Hoje.AddDays(-1));
        anterior.Ate.ShouldBe(Hoje.AddDays(-1));
    }
}
