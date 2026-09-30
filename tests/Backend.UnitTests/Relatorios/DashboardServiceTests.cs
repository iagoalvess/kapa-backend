using Backend.Business.Relatorios.Interfaces;
using Backend.Business.Relatorios.Models;
using Backend.Business.Relatorios.Services;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Relatorios;

/// <summary>
/// O painel do formando: adimplência e gasto por fornecedor, como o repositório os devolve.
/// </summary>
public sealed class DashboardServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRelatorioRepository _relatorios = Substitute.For<IRelatorioRepository>();

    private DashboardService Servico => new(_relatorios);

    [Fact]
    public async Task O_painel_repassa_adimplencia_e_fornecedores_sem_recalculo()
    {
        // Arrange
        var adimplencia = new Adimplencia(1_000_00L, 985_00L);
        _relatorios.Adimplencia(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(adimplencia);
        _relatorios.PorFornecedor(Arg.Any<PeriodoDoRelatorio?>(), Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var painel = (await Servico.Publico(Ct)).Valor;

        // Assert
        painel.Adimplencia.ShouldBe(adimplencia);
        painel.PorFornecedor.ShouldBeEmpty();
    }

    /// <summary>
    /// Turma sem nada vencido é 100%, e não uma divisão por zero.
    /// </summary>
    /// <remarks>
    /// É o caso de toda turma no primeiro mês — e um painel que abre com "0% de adimplência" é o que
    /// faz a comissão desconfiar do sistema antes de usá-lo.
    /// </remarks>
    [Theory]
    [InlineData(0L, 0L, 10_000)]
    [InlineData(1_000_00L, 1_000_00L, 10_000)]
    [InlineData(1_000_00L, 985_00L, 9_850)]
    [InlineData(1_000_00L, 0L, 0)]
    public void A_adimplencia_e_recebido_sobre_devido_em_base_dez_mil(long devido, long recebido, int esperado) =>
        new Adimplencia(devido, recebido).PercentualBaseDezMil.ShouldBe(esperado);
}
