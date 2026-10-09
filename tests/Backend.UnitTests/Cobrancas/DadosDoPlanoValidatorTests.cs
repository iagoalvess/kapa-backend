using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Validators;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>O teto do atraso que os Termos prometem: 2% de multa e 1% de juros ao mês.</summary>
public sealed class DadosDoPlanoValidatorTests
{
    private readonly DadosDoPlanoValidator _validator = new();

    [Theory]
    [InlineData(200, 100, true)]
    [InlineData(201, 100, false)]
    [InlineData(200, 101, false)]
    public void Multa_e_juros_respeitam_o_teto(int multa, int juros, bool valido) =>
        _validator.Validate(new DadosDoPlano("Plano 2027", multa, juros, 0, 0, 0)).IsValid.ShouldBe(valido);
}
