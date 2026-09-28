using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Validators;
using Backend.Business.Common.Datas;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>
/// Que tipo cabe onde: o plano cobra a turma inteira, o opcional só quem pede — e o convite extra é
/// dos dois.
/// </summary>
public sealed class TiposDeCobrancaTests
{
    private static DadosDoItem Item(TipoDeCobranca tipo) => new(tipo, null, 18_000, 1, 10, DataUtils.Hoje().AddMonths(1));

    [Theory]
    [InlineData(TipoDeCobranca.Mesa)]
    [InlineData(TipoDeCobranca.FotoEAlbum)]
    [InlineData(TipoDeCobranca.Outro)]
    public void Tipo_de_opcional_e_recusado_no_plano(TipoDeCobranca tipo)
    {
        var resultado = new DadosDoItemValidator().Validate(Item(tipo));

        resultado.Errors.ShouldContain(erro => erro.ErrorCode == "cobranca.tipo_invalido");
    }

    [Theory]
    [InlineData(TipoDeCobranca.Mensalidade)]
    [InlineData(TipoDeCobranca.Adesao)]
    [InlineData(TipoDeCobranca.Avulsa)]
    public void Tipo_do_plano_e_recusado_no_opcional(TipoDeCobranca tipo)
    {
        var resultado = new DadosDoOpcionalValidator().Validate(new DadosDoOpcional(Item(tipo)));

        resultado.Errors.ShouldContain(erro => erro.ErrorCode == "cobranca.tipo_invalido");
    }

    [Fact]
    public void Convite_extra_cabe_nos_dois()
    {
        new DadosDoItemValidator().Validate(Item(TipoDeCobranca.ConviteExtra)).IsValid.ShouldBeTrue();
        new DadosDoOpcionalValidator().Validate(new DadosDoOpcional(Item(TipoDeCobranca.ConviteExtra))).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Mesa_e_um_opcional_valido()
    {
        new DadosDoOpcionalValidator().Validate(new DadosDoOpcional(Item(TipoDeCobranca.Mesa))).IsValid.ShouldBeTrue();
    }

    /// <summary>Sem rótulo próprio, o tipo sem acento chegaria ao e-mail e ao PDF ("FotoEAlbum").</summary>
    [Fact]
    public void Tipos_de_duas_palavras_ou_com_acento_tem_rotulo()
    {
        RotuloDoItem.De(TipoDeCobranca.FotoEAlbum, null).ShouldBe("Foto e álbum");
        RotuloDoItem.De(TipoDeCobranca.Vestuario, null).ShouldBe("Vestuário");
    }
}
