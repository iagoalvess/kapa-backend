using Backend.Business.Cobrancas.Models;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>
/// O valor da parcela no dia: multa e juros depois da carência, desconto antes do vencimento — calculado
/// na leitura, nunca gravado (decisão 7 da Sprint 9; P4 e P5 de 14/09/2026).
/// </summary>
public sealed class ValorDoDiaTests
{
    private static readonly DateOnly Vencimento = new(2026, 4, 10);

    /// <summary>Multa de 2%, juros de 1% ao mês, sem carência, 5% de desconto — o plano típico.</summary>
    private static readonly RegrasDeAtraso Tipicas = new(200, 100, 0, 500);

    [Theory]
    [InlineData(1, 7_000, 117)]
    [InlineData(30, 7_000, 3_500)]
    [InlineData(90, 7_000, 10_500)]
    public void Atraso_soma_multa_uma_vez_e_juros_pro_rata_desde_o_vencimento(int dias, long multa, long juros)
    {
        // Act
        var valor = ValorDoDia.Calcular(350_000, Vencimento, Vencimento.AddDays(dias), Tipicas);

        // Assert
        valor.MultaEmCentavos.ShouldBe(multa);
        valor.JurosEmCentavos.ShouldBe(juros);
        valor.DescontoEmCentavos.ShouldBe(0);
        valor.DiasDeAtraso.ShouldBe(dias);
        valor.TotalEmCentavos.ShouldBe(350_000 + multa + juros);
    }

    [Fact]
    public void No_dia_do_vencimento_vale_o_original()
    {
        // Act
        var valor = ValorDoDia.Calcular(350_000, Vencimento, Vencimento, Tipicas);

        // Assert
        valor.TotalEmCentavos.ShouldBe(350_000);
        valor.DiasDeAtraso.ShouldBe(0);
    }

    [Fact]
    public void Antes_do_vencimento_aplica_o_desconto_por_antecipacao()
    {
        // Act
        var valor = ValorDoDia.Calcular(350_000, Vencimento, Vencimento.AddDays(-1), Tipicas);

        // Assert
        valor.DescontoEmCentavos.ShouldBe(17_500);
        valor.TotalEmCentavos.ShouldBe(332_500);
    }

    /// <summary>P5: dentro da carência, nada; passou dela, juros de todos os dias desde o vencimento.</summary>
    [Fact]
    public void Carencia_e_tolerancia_e_passada_ela_os_juros_contam_desde_o_vencimento()
    {
        // Arrange
        var comCarencia = Tipicas with
        {
            CarenciaEmDias = 5,
        };

        // Act
        var dentro = ValorDoDia.Calcular(300_000, Vencimento, Vencimento.AddDays(5), comCarencia);
        var depois = ValorDoDia.Calcular(300_000, Vencimento, Vencimento.AddDays(6), comCarencia);

        // Assert
        dentro.TotalEmCentavos.ShouldBe(300_000);
        depois.MultaEmCentavos.ShouldBe(6_000);
        depois.JurosEmCentavos.ShouldBe(600);
    }

    [Fact]
    public void Juros_arredondam_para_o_centavo_mais_proximo()
    {
        // Arrange
        var soJuros = new RegrasDeAtraso(0, 100, 0, 0);

        // Act
        var valor = ValorDoDia.Calcular(10_050, Vencimento, Vencimento.AddDays(1), soJuros);

        // Assert
        valor.JurosEmCentavos.ShouldBe(3);
    }

    [Fact]
    public void Sem_regras_a_parcela_vale_o_original_em_qualquer_dia()
    {
        // Act
        var antes = ValorDoDia.Calcular(350_000, Vencimento, Vencimento.AddDays(-10), RegrasDeAtraso.Nenhuma);
        var depois = ValorDoDia.Calcular(350_000, Vencimento, Vencimento.AddDays(90), RegrasDeAtraso.Nenhuma);

        // Assert
        antes.TotalEmCentavos.ShouldBe(350_000);
        depois.TotalEmCentavos.ShouldBe(350_000);
    }

    [Fact]
    public void Valor_negativo_da_bolsa_nao_tem_encargo_nem_desconto()
    {
        // Act
        var valor = ValorDoDia.Calcular(-50_000, Vencimento, Vencimento.AddDays(30), Tipicas);

        // Assert
        valor.TotalEmCentavos.ShouldBe(-50_000);
    }
}
