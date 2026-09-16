using Backend.Api.DTOs.Cobrancas;
using Backend.Business.Cobrancas.Models;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>
/// Nenhum valor de cobrança trafega nem é gravado em ponto flutuante.
/// </summary>
/// <remarks>
/// Varre os modelos e os DTOs da feature: propriedade nova em <c>double</c> ou <c>float</c> quebra
/// aqui, antes de o centavo sumir num arredondamento.
/// </remarks>
public sealed class DinheiroEmCentavosTests
{
    private static readonly Type[] PontoFlutuante = [typeof(double), typeof(float), typeof(double?), typeof(float?)];

    [Fact]
    public void Modelos_e_dtos_de_cobranca_nao_tem_ponto_flutuante()
    {
        // Arrange
        var tipos = typeof(Parcela)
            .Assembly.GetTypes()
            .Where(tipo => tipo.Namespace == typeof(Parcela).Namespace)
            .Concat(typeof(ParcelaDTO).Assembly.GetTypes().Where(tipo => tipo.Namespace == typeof(ParcelaDTO).Namespace))
            .ToList();

        // Act
        var flutuantes = tipos
            .SelectMany(tipo => tipo.GetProperties().Where(propriedade => PontoFlutuante.Contains(propriedade.PropertyType)))
            .Select(propriedade => $"{propriedade.DeclaringType!.Name}.{propriedade.Name}")
            .ToList();

        // Assert
        tipos.ShouldContain(typeof(ItemDeCobranca));
        tipos.ShouldContain(typeof(SimulacaoDoPlanoDTO));
        flutuantes.ShouldBeEmpty();
    }
}
