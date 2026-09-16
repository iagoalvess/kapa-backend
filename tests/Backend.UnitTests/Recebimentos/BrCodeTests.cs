using Backend.Business.Recebimentos.Services;
using Shouldly;

namespace Backend.UnitTests.Recebimentos;

/// <summary>O copia-e-cola do PIX, conferido contra o exemplo do manual do BR Code do Banco Central.</summary>
public sealed class BrCodeTests
{
    private const string ChaveDoManual = "123e4567-e12b-12d1-a456-426655440000";

    /// <summary>O exemplo do manual, byte a byte — o CRC <c>1D3D</c> incluso.</summary>
    [Fact]
    public void Reproduz_o_exemplo_do_manual_do_Banco_Central()
    {
        BrCode
            .Montar(ChaveDoManual, "Fulano de Tal", "BRASILIA")
            .ShouldBe(
                "00020126580014br.gov.bcb.pix0136123e4567-e12b-12d1-a456-4266554400005204000053039865802BR5913Fulano de Tal6008BRASILIA62070503***63041D3D"
            );
    }

    /// <summary>
    /// O nome e a cidade perdem o acento e são cortados <b>antes</b> do CRC: o resultado é idêntico ao
    /// de quem já tivesse digitado o texto limpo — e esse CRC é o do algoritmo que reproduz o manual.
    /// </summary>
    [Fact]
    public void Nome_e_cidade_saem_sem_acento_e_cortados_com_o_crc_do_texto_limpo()
    {
        var comAcento = BrCode.Montar(ChaveDoManual, "José Conceição da Silva Sauro Pereira", "São José dos Pinhais");
        var limpo = BrCode.Montar(ChaveDoManual, "Jose Conceicao da Silva S", "Sao Jose dos Pi");

        comAcento.ShouldBe(limpo);
        comAcento.ShouldContain("5925Jose Conceicao da Silva S6015Sao Jose dos Pi");
    }

    [Fact]
    public void Corte_que_cai_num_espaco_nao_deixa_espaco_no_fim()
    {
        BrCode.Texto("Associação dos Formandos  de 2027", BrCode.TamanhoMaximoDoNome).ShouldBe("Associacao dos Formandos");
    }

    [Theory]
    [InlineData(100, "54041.00")]
    [InlineData(123_456, "54071234.56")]
    public void Valor_sai_em_reais_com_duas_casas_e_ponto(long centavos, string campo)
    {
        BrCode.Montar(ChaveDoManual, "Fulano de Tal", "BRASILIA", centavos).ShouldContain($"5303986{campo}5802BR");
    }

    [Theory]
    [InlineData("parcela-0193.abc", "62180514parcela0193abc")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123", "62290525ABCDEFGHIJKLMNOPQRSTUVWXY")]
    [InlineData(null, "62070503***")]
    [InlineData("--", "62070503***")]
    public void Identificador_fica_so_com_letras_e_numeros_ate_25(string? identificador, string campo)
    {
        BrCode.Montar(ChaveDoManual, "Fulano de Tal", "BRASILIA", identificador: identificador).ShouldContain($"{campo}6304");
    }
}
