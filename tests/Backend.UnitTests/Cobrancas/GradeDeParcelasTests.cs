using Backend.Business.Cobrancas.Models;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>A grade: divisão em centavos, resto na primeira parcela, vencimento no mês curto.</summary>
public sealed class GradeDeParcelasTests
{
    private static DadosDoItem Item(long valor, int parcelas, int dia = 10, DateOnly? primeiroMes = null) =>
        new(TipoDeCobranca.Mensalidade, null, valor, parcelas, dia, primeiroMes ?? new DateOnly(2026, 3, 1));

    [Fact]
    public void Vinte_e_quatro_parcelas_de_8400_fecham_exatamente()
    {
        // Act
        var grade = Item(840_000, 24).Grade();

        // Assert
        grade.Count.ShouldBe(24);
        grade.ShouldAllBe(parcela => parcela.ValorEmCentavos == 35_000);
        grade.Sum(parcela => parcela.ValorEmCentavos).ShouldBe(840_000);
    }

    [Fact]
    public void Resto_da_divisao_vai_para_a_primeira_parcela()
    {
        // Act
        var grade = Item(100_000, 3).Grade();

        // Assert
        grade.Select(parcela => parcela.ValorEmCentavos).ShouldBe([33_334, 33_333, 33_333]);
        grade.Sum(parcela => parcela.ValorEmCentavos).ShouldBe(100_000);
    }

    /// <summary>O gancho da bolsa: valor negativo divide igual, e a soma fecha.</summary>
    [Fact]
    public void Valor_negativo_tambem_fecha_no_centavo()
    {
        // Act
        var grade = Item(-100_000, 3).Grade();

        // Assert
        grade.Select(parcela => parcela.ValorEmCentavos).ShouldBe([-33_334, -33_333, -33_333]);
    }

    [Fact]
    public void Vencimentos_sao_mensais_a_partir_do_primeiro_mes_e_atravessam_o_ano()
    {
        // Act
        var grade = Item(40_000, 4, dia: 10, primeiroMes: new DateOnly(2026, 11, 20)).Grade();

        // Assert
        grade.Select(parcela => parcela.Vencimento).ShouldBe([new(2026, 11, 10), new(2026, 12, 10), new(2027, 1, 10), new(2027, 2, 10)]);
        grade.Select(parcela => parcela.Numero).ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void Dia_31_vira_o_ultimo_dia_do_mes_inclusive_em_fevereiro_bissexto()
    {
        // Act
        var grade = Item(50_000, 5, dia: 31, primeiroMes: new DateOnly(2028, 1, 1)).Grade();

        // Assert
        grade
            .Select(parcela => parcela.Vencimento)
            .ShouldBe([new(2028, 1, 31), new(2028, 2, 29), new(2028, 3, 31), new(2028, 4, 30), new(2028, 5, 31)]);
    }

    [Theory]
    [InlineData(2027, 28)]
    [InlineData(2028, 29)]
    public void Dia_30_em_fevereiro_cai_no_ultimo_dia(int ano, int ultimoDia)
    {
        // Act
        var vencimento = GradeDeParcelas.Vencimento(new DateOnly(ano, 2, 1), 30);

        // Assert
        vencimento.ShouldBe(new DateOnly(ano, 2, ultimoDia));
    }
}
