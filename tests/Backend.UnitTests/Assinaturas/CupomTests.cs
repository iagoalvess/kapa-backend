using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Validators;
using Backend.Business.Common.Datas;
using Shouldly;

namespace Backend.UnitTests.Assinaturas;

/// <summary>O desconto do cupom e a forma do cupom novo (Sprint 51).</summary>
public sealed class CupomTests
{
    [Theory]
    [InlineData(8900, 50, 4450)]
    [InlineData(17900, 10, 16110)]
    [InlineData(999, 50, 500)]
    [InlineData(85400, 15, 72590)]
    public void O_desconto_arredonda_a_favor_da_turma(long cheio, int percentual, long esperado) =>
        Cupom.ComDesconto(cheio, percentual).ShouldBe(esperado);

    [Theory]
    [InlineData("piloto50", true)]
    [InlineData("  ufmg-2027 ", true)]
    [InlineData("ABC12", false)]
    [InlineData("COM ESPACO", false)]
    [InlineData("ÇUPOM10", false)]
    [InlineData("A23456789012345678901", false)]
    public void Forma_do_codigo(string digitado, bool valida) => Cupom.FormaValida(Cupom.Normalizar(digitado)).ShouldBe(valida);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    [InlineData(100, false)]
    public void Percentual_vai_de_1_a_50(int percentual, bool valido) =>
        new NovoCupomValidator().Validate(new NovoCupom("PILOTO50", percentual, DataUtils.Hoje(), 10)).IsValid.ShouldBe(valido);

    [Fact]
    public void Cupom_esgotado_vencido_ou_desativado_nao_esta_disponivel()
    {
        // Arrange
        var agora = DateTime.UtcNow;
        var vencido = new Cupom { ValidoAte = agora.AddSeconds(-1), LimiteDeUsos = 1 };
        var desativado = new Cupom { ValidoAte = agora.AddDays(1), LimiteDeUsos = 1 };
        desativado.Desativar();

        // Assert
        new Cupom { ValidoAte = agora.AddDays(1), LimiteDeUsos = 1 }
            .Disponivel(agora)
            .ShouldBeTrue();
        new Cupom { ValidoAte = agora.AddDays(1), LimiteDeUsos = 0 }
            .Disponivel(agora)
            .ShouldBeFalse();
        vencido.Disponivel(agora).ShouldBeFalse();
        desativado.Disponivel(agora).ShouldBeFalse();
    }
}
