using Backend.Business.Common.Texto;
using Shouldly;

namespace Backend.UnitTests.Formandos;

/// <summary>CPF, telefone, CEP e UF: o que o validator aceita e a forma que a entidade grava.</summary>
public sealed class FormatosBrasileirosTests
{
    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("529.982.247-24", false)]
    [InlineData("111.111.111-11", false)]
    [InlineData("5299822472", false)]
    [InlineData("", false)]
    public void Cpf_confere_os_dois_digitos_verificadores(string cpf, bool esperado) => FormatosBrasileiros.CpfValido(cpf).ShouldBe(esperado);

    [Theory]
    [InlineData("(41) 99876-5432", "+5541998765432")]
    [InlineData("41 3333-4444", "+554133334444")]
    [InlineData("+55 41 99876-5432", "+5541998765432")]
    [InlineData("+1 415 555 0100", "+14155550100")]
    [InlineData("(41) 89876-5432", null)]
    [InlineData("(01) 3333-4444", null)]
    [InlineData("99876-5432", null)]
    [InlineData("+0 1234 5678", null)]
    public void Telefone_nacional_ou_E164_sai_em_E164(string telefone, string? esperado) =>
        FormatosBrasileiros.TelefoneE164(telefone).ShouldBe(esperado);

    [Theory]
    [InlineData("80000-000", true)]
    [InlineData("80.000-000", true)]
    [InlineData("80000000", true)]
    [InlineData("8000-0000", false)]
    [InlineData("800000001", false)]
    public void Cep_tem_oito_digitos(string cep, bool esperado) => FormatosBrasileiros.CepValido(cep).ShouldBe(esperado);

    [Theory]
    [InlineData("pr", true)]
    [InlineData("SP", true)]
    [InlineData("XX", false)]
    public void Uf_e_uma_das_27_siglas(string uf, bool esperado) => FormatosBrasileiros.UfValida(uf).ShouldBe(esperado);
}
