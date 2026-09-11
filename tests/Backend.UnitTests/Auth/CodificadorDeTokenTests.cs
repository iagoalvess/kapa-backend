using Backend.Business.Auth.Services;
using Shouldly;

namespace Backend.UnitTests.Auth;

/// <summary>
/// Garante que o token sobrevive à ida e volta por uma URL.
/// </summary>
/// <remarks>
/// É a causa mais comum do "o link de redefinição não funciona": o token do Identity é Base64
/// comum, e o <c>+</c> dele vira espaço ao atravessar uma query string.
/// </remarks>
public sealed class CodificadorDeTokenTests
{
    [Theory]
    [InlineData("CfDJ8Abc+123/xyz==")]
    [InlineData("token simples")]
    [InlineData("com+mais/barras==e==iguais")]
    [InlineData("acentuação e cedilha ç")]
    public void Codificar_e_decodificar_devolve_o_token_original(string original)
    {
        CodificadorDeToken.Decodificar(CodificadorDeToken.Codificar(original)).ShouldBe(original);
    }

    [Fact]
    public void O_token_codificado_nao_contem_caractere_problematico_em_URL()
    {
        var codificado = CodificadorDeToken.Codificar("CfDJ8Abc+123/xyz==");

        codificado.ShouldNotContain("+");
        codificado.ShouldNotContain("/");
        codificado.ShouldNotContain("=");
    }

    [Theory]
    [InlineData("não é base64 !!!")]
    [InlineData("%%%")]
    public void Valor_invalido_devolve_nulo_em_vez_de_lancar(string invalido)
    {
        CodificadorDeToken.Decodificar(invalido).ShouldBeNull();
    }
}
