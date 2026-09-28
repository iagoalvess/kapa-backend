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

    /// <summary>
    /// "5% para quem quitar à vista" não pode virar 5% para quem pagou no dia 9 em vez do dia 10.
    /// </summary>
    [Theory]
    [InlineData(1, 350_000)]
    [InlineData(29, 350_000)]
    [InlineData(30, 332_500)]
    [InlineData(60, 332_500)]
    public void Desconto_exige_a_antecedencia_combinada(int diasAntes, long esperado)
    {
        // Arrange
        var regras = new RegrasDeAtraso(200, 100, 0, 500, DiasMinimosParaDesconto: 30);

        // Act
        var valor = ValorDoDia.Calcular(350_000, Vencimento, Vencimento.AddDays(-diasAntes), regras);

        // Assert
        valor.TotalEmCentavos.ShouldBe(esperado);
    }

    /// <summary>Snapshot assinado antes de 17/09/2026 não tem o campo: zero, e a regra é a que aquela pessoa aceitou.</summary>
    [Fact]
    public void Sem_antecedencia_minima_qualquer_dia_antes_vale_o_desconto()
    {
        // Act
        var valor = ValorDoDia.Calcular(350_000, Vencimento, Vencimento.AddDays(-1), new RegrasDeAtraso(200, 100, 0, 500));

        // Assert
        valor.TotalEmCentavos.ShouldBe(332_500);
    }

    /// <summary>O pagamento parcial abate; o devido do dia, que a divergência compara, continua cheio.</summary>
    [Fact]
    public void O_que_ja_foi_pago_abate_do_valor_do_dia()
    {
        // Act
        var valor = ValorDoDia.Calcular(350_000, Vencimento, Vencimento, Tipicas, jaPagoEmCentavos: 200_000);

        // Assert
        valor.TotalEmCentavos.ShouldBe(150_000);
        valor.DevidoEmCentavos.ShouldBe(350_000);
    }

    /// <summary>Pagar acima do devido não vira crédito na parcela — a sobra é divergência.</summary>
    [Fact]
    public void Pago_acima_do_devido_zera_o_valor_do_dia_sem_ficar_negativo()
    {
        // Act
        var valor = ValorDoDia.Calcular(350_000, Vencimento, Vencimento, Tipicas, jaPagoEmCentavos: 400_000);

        // Assert
        valor.TotalEmCentavos.ShouldBe(0);
    }

    /// <summary>
    /// A leitura do extrato abate o pago em parcial, como a entidade: sem isso, quem pagou metade via
    /// o valor cheio no extrato e no PIX, e pagava tudo de novo.
    /// </summary>
    [Fact]
    public void Resumo_de_parcela_com_pagamento_parcial_cobra_so_o_que_falta()
    {
        // Arrange
        var parcela = new ParcelaResumo(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Ana",
            Guid.CreateVersion7(),
            TipoDeCobranca.Mensalidade,
            null,
            1,
            12,
            Vencimento,
            350_000,
            StatusDaParcela.Aberta,
            ValorPagoEmCentavos: 200_000
        );

        // Act
        var comValor = parcela.ComValorDoDia(Vencimento, Tipicas);

        // Assert
        comValor.ValorDoDia!.TotalEmCentavos.ShouldBe(150_000);
    }
}
