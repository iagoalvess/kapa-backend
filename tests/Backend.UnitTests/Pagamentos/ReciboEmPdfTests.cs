using System.Text;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Pagamentos.Services;
using Backend.Business.Recebimentos.Models;
using Shouldly;

namespace Backend.UnitTests.Pagamentos;

/// <summary>
/// O que o recibo garante sem banco: o mesmo recebimento dá os mesmos bytes, cabe numa página, mostra
/// a diferença quando há, e diz que não é documento fiscal.
/// </summary>
public sealed class ReciboEmPdfTests
{
    private static readonly MeiosDaConta Meios = new(
        new ChavePixDaConta(TipoDeChavePix.Cpf, "52998224725", "ANA CAROLINA S MARTINS", "Curitiba"),
        new DadosBancarios("Nubank", "0001", "123-4", "Corrente", "Comissão Medicina"),
        new DinheiroComAlguem("Tesa", null)
    );

    private static DadosDoRecibo Recibo(long recebido = 35_000, FormaDePagamento forma = FormaDePagamento.Pix) =>
        new(
            new Guid("01928a4b-0000-7000-8000-000000000001"),
            Guid.Empty,
            "Medicina 2027",
            "UFPR",
            new ParcelaResumo(
                Guid.Empty,
                Guid.Empty,
                Guid.Empty,
                "Beatriz Lima",
                Guid.Empty,
                TipoDeCobranca.Mensalidade,
                null,
                3,
                24,
                new DateOnly(2026, 9, 10),
                35_000,
                StatusDaParcela.Paga
            ),
            "52998224725",
            forma,
            recebido,
            35_000,
            new DateOnly(2026, 9, 12),
            "Tesa Souza",
            new DateTime(2026, 9, 12, 18, 30, 0, DateTimeKind.Utc),
            false
        );

    /// <summary>Em texto: o conteúdo do PDF não é comprimido, e o que não é ASCII vai em octal.</summary>
    private static string Texto(byte[] pdf) => Encoding.ASCII.GetString(pdf);

    [Fact]
    public void Mesmo_recebimento_gera_os_mesmos_bytes_numa_pagina()
    {
        // Act
        var primeiro = ReciboEmPdf.Gerar(Recibo(), Meios, mascararCpf: false);
        var segundo = ReciboEmPdf.Gerar(Recibo(), Meios, mascararCpf: false);

        // Assert
        segundo.ShouldBe(primeiro);
        Texto(primeiro).ShouldContain("/Count 1 ");
        Texto(primeiro).ShouldContain("ANA CAROLINA S MARTINS \\267 CPF ***.982.247-**");
        Texto(primeiro).ShouldContain("n\\343o \\351 documento fiscal");
        Texto(primeiro).ShouldContain("01928a4b-0000-7000-8000-000000000001");
    }

    [Fact]
    public void Pago_a_menos_mostra_os_dois_valores_e_a_diferenca()
    {
        // Act
        var texto = Texto(ReciboEmPdf.Gerar(Recibo(recebido: 30_000), Meios, mascararCpf: true));

        // Assert
        texto.ShouldContain("R$ 350,00");
        texto.ShouldContain("R$ 300,00");
        texto.ShouldContain("R$ -50,00");
        texto.ShouldContain("R$ 50,00 menor que o devido");
    }

    [Fact]
    public void Sem_diferenca_nao_ha_linha_de_diferenca()
    {
        // Act
        var texto = Texto(ReciboEmPdf.Gerar(Recibo(), Meios, mascararCpf: true));

        // Assert
        texto.ShouldNotContain("Diferen");
    }

    [Theory]
    [InlineData(FormaDePagamento.Transferencia, "Comiss\\343o Medicina \\267 Nubank")]
    [InlineData(FormaDePagamento.Dinheiro, "Tesa, em m\\343os")]
    [InlineData(FormaDePagamento.Outro, "a turma Medicina 2027")]
    public void Quem_recebeu_segue_a_forma_do_pagamento(FormaDePagamento forma, string esperado)
    {
        // Act
        var texto = Texto(ReciboEmPdf.Gerar(Recibo(forma: forma), Meios, mascararCpf: true));

        // Assert
        texto.ShouldContain(esperado);
    }
}
