using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Relatorios.Interfaces;
using Backend.Business.Relatorios.Models;
using Backend.Business.Relatorios.Services;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Relatorios;

/// <summary>
/// Os números do painel são os mesmos do caixa — nenhum é recalculado aqui (decisão 4 da Sprint 12).
/// </summary>
public sealed class DashboardServiceTests
{
    private static readonly Guid FormaturaId = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ICaixaService _caixa = Substitute.For<ICaixaService>();
    private readonly IRelatorioRepository _relatorios = Substitute.For<IRelatorioRepository>();

    private DashboardService Servico => new(_caixa, _relatorios);

    [Fact]
    public async Task Os_numeros_do_painel_sao_os_do_caixa_sem_recalculo()
    {
        // Arrange
        Consolidado(arrecadado: 184_500_00L, gasto: 96_200_00L);

        // Act
        var painel = (await Servico.Publico(FormaturaId, Ct)).Valor;

        // Assert
        painel.Caixa.ArrecadadoEmCentavos.ShouldBe(184_500_00L);
        painel.Caixa.GastoEmCentavos.ShouldBe(96_200_00L);
        painel.Caixa.SaldoEmCentavos.ShouldBe(88_300_00L);
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

    /// <summary>O caixa devolvendo os valores pedidos, com o resto zerado.</summary>
    /// <param name="arrecadado">O que entrou.</param>
    /// <param name="gasto">O que saiu.</param>
    private void Consolidado(long arrecadado = 0, long gasto = 0)
    {
        _caixa.Consolidado(Arg.Any<CancellationToken>()).Returns(new CaixaConsolidado(arrecadado, gasto, 0, 0, 0, [], [], []));
        _caixa.Projecao(FormaturaId, Arg.Any<CancellationToken>()).Returns(new ProjecaoDoCaixa([], arrecadado - gasto, 0));
        _relatorios.Adimplencia(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Adimplencia.Integral);
        _relatorios.PorFornecedor(Arg.Any<PeriodoDoRelatorio?>(), Arg.Any<CancellationToken>()).Returns([]);
    }
}
