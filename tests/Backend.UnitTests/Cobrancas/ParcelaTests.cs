using Backend.Business.Cobrancas.Models;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>
/// O pagamento parcial: a parcela só fecha quando o dinheiro cobre o que ela cobra.
/// </summary>
/// <remarks>
/// Até 17/09/2026 qualquer valor a fechava, e quem pagava R$ 200 de R$ 350 saía do "a receber"
/// devendo R$ 150 que só apareciam na aba Divergências.
/// </remarks>
public sealed class ParcelaTests
{
    private static readonly DateOnly Vencimento = new(2027, 4, 10);

    private static readonly RegrasDeAtraso Tipicas = new(200, 100, 0, 0);

    private static Parcela Nova() => Parcela.Nova(Guid.CreateVersion7(), Guid.CreateVersion7(), new ParcelaPrevista(1, Vencimento, 350_000));

    [Fact]
    public void Pagamento_parcial_deixa_a_parcela_aberta_com_o_saldo()
    {
        // Arrange
        var parcela = Nova();

        // Act
        var quitou = parcela.Pagar(200_000, Vencimento, 350_000);

        // Assert
        quitou.Valor.ShouldBeFalse();
        parcela.Status.ShouldBe(StatusDaParcela.Aberta);
        parcela.PagoEm.ShouldBeNull();
        parcela.ValorPagoEmCentavos.ShouldBe(200_000);
        parcela.ValorEm(Vencimento, Tipicas).TotalEmCentavos.ShouldBe(150_000);
    }

    [Fact]
    public void Somados_os_parciais_alcancam_o_devido_e_a_parcela_fecha()
    {
        // Arrange
        var parcela = Nova();
        parcela.Pagar(200_000, Vencimento, 350_000);

        // Act
        var quitou = parcela.Pagar(150_000, Vencimento.AddDays(3), 350_000);

        // Assert
        quitou.Valor.ShouldBeTrue();
        parcela.Status.ShouldBe(StatusDaParcela.Paga);
        parcela.PagoEm.ShouldBe(Vencimento.AddDays(3));
        parcela.ValorPagoEmCentavos.ShouldBe(350_000);
    }

    /// <summary>
    /// Quem paga o principal de uma parcela vencida encerra a dívida; multa e juros não pagos são
    /// divergência, não um saldo de R$ 10,50 que deixaria a parcela aberta para sempre.
    /// </summary>
    [Fact]
    public void Pagar_o_original_de_uma_vencida_fecha_a_parcela()
    {
        // Arrange
        var parcela = Nova();

        // Act
        var quitou = parcela.Pagar(350_000, Vencimento.AddDays(30), devidoEmCentavos: 360_500);

        // Assert
        quitou.Valor.ShouldBeTrue();
        parcela.Status.ShouldBe(StatusDaParcela.Paga);
    }

    /// <summary>E quem paga adiantado paga o devido com desconto, não o original.</summary>
    [Fact]
    public void Pagar_o_devido_com_desconto_fecha_a_parcela()
    {
        // Arrange
        var parcela = Nova();

        // Act
        var quitou = parcela.Pagar(332_500, Vencimento.AddDays(-30), devidoEmCentavos: 332_500);

        // Assert
        quitou.Valor.ShouldBeTrue();
        parcela.Status.ShouldBe(StatusDaParcela.Paga);
    }

    [Fact]
    public void Estorno_tira_so_o_valor_da_baixa_desfeita()
    {
        // Arrange
        var parcela = Nova();
        parcela.Pagar(200_000, Vencimento, 350_000);
        parcela.Pagar(150_000, Vencimento, 350_000);

        // Act
        var estorno = parcela.Estornar(150_000);

        // Assert
        estorno.Sucesso.ShouldBeTrue();
        parcela.Status.ShouldBe(StatusDaParcela.Aberta);
        parcela.PagoEm.ShouldBeNull();
        parcela.ValorPagoEmCentavos.ShouldBe(200_000);
    }

    [Fact]
    public void Estorno_da_ultima_baixa_zera_o_pago()
    {
        // Arrange
        var parcela = Nova();
        parcela.Pagar(350_000, Vencimento, 350_000);

        // Act
        parcela.Estornar(350_000);

        // Assert
        parcela.ValorPagoEmCentavos.ShouldBeNull();
        parcela.ValorEm(Vencimento, Tipicas).TotalEmCentavos.ShouldBe(350_000);
    }

    [Fact]
    public void Parcela_sem_pagamento_nao_tem_o_que_estornar()
    {
        // Act
        var estorno = Nova().Estornar(100);

        // Assert
        estorno.PrimeiroErro.Codigo.ShouldBe("pagamento.parcela_nao_paga");
    }

    /// <summary>
    /// Sprint 48, D12: enquanto a comissão não responde, a vencida suspensa se lê como aberta; vencido o prazo de
    /// resposta, volta a ser vencida sozinha.
    /// </summary>
    [Fact]
    public void Suspensa_se_le_aberta_ate_o_fim_do_prazo()
    {
        // Arrange
        var parcela = Nova();
        var depois = Vencimento.AddDays(3);

        // Act
        parcela.Suspender(Vencimento.AddDays(7));

        // Assert
        Parcela.StatusNoDia(parcela.Status, parcela.Vencimento, depois, parcela.SuspensaAte).ShouldBe(StatusDaParcela.Aberta);
        Parcela.StatusNoDia(parcela.Status, parcela.Vencimento, Vencimento.AddDays(8), parcela.SuspensaAte).ShouldBe(StatusDaParcela.Vencida);
        parcela.StatusEm(depois).ShouldBe(StatusDaParcela.Vencida);
    }

    /// <summary>Aprovado o cancelamento do pacote (D9), a paga deixa de valer e o pago fica nela, para a lista "a devolver".</summary>
    [Fact]
    public void Desfazer_cancela_ate_a_paga_e_guarda_o_que_entrou()
    {
        // Arrange
        var parcela = Nova();
        parcela.Pagar(350_000, Vencimento, 350_000);
        parcela.Suspender(Vencimento.AddDays(7));

        // Act
        var desfeita = parcela.Desfazer();

        // Assert
        desfeita.ShouldBeTrue();
        parcela.Status.ShouldBe(StatusDaParcela.Cancelada);
        parcela.ValorPagoEmCentavos.ShouldBe(350_000);
        parcela.Desfazer().ShouldBeFalse();
    }
}
