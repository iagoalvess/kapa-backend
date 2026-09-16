using Backend.Business.Common.Datas;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Financeiro.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Financeiro;

/// <summary>
/// O caixa como conta, e não como coluna: saldo é arrecadado menos gasto, despesa prevista fica de
/// fora dele, e a projeção soma mês a mês só o que ainda vence.
/// </summary>
public sealed class CaixaServiceTests
{
    private static readonly Guid FormaturaId = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ICaixaRepository _caixa = Substitute.For<ICaixaRepository>();
    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();

    private readonly DateOnly _hoje = DataUtils.Hoje();

    private CaixaService Servico => new(_caixa, _formaturas);

    private DateOnly MesAtual => new(_hoje.Year, _hoje.Month, 1);

    [Fact]
    public async Task Saldo_e_arrecadado_menos_gasto_e_a_despesa_prevista_fica_de_fora()
    {
        // Arrange
        _caixa.Arrecadado(Arg.Any<CancellationToken>()).Returns(184_500_00L);
        _caixa.Gasto(Arg.Any<CancellationToken>()).Returns(96_200_00L);
        _caixa.DespesasPrevistas(Arg.Any<CancellationToken>()).Returns(40_000_00L);
        _caixa.ParcelasEmAberto(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns((212_000_00L, 8_000_00L));
        _caixa.PorCategoria(Arg.Any<CancellationToken>()).Returns([]);
        _caixa.UltimosLancamentos(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var caixa = (await Servico.Consolidado(Ct)).Valor;

        // Assert
        caixa.SaldoEmCentavos.ShouldBe(88_300_00L);
        caixa.AReceberEmCentavos.ShouldBe(212_000_00L);
        caixa.EmAtrasoEmCentavos.ShouldBe(8_000_00L);
        caixa.APagarEmCentavos.ShouldBe(40_000_00L);
        caixa.SaldoProjetadoEmCentavos.ShouldBe(88_300_00L + 212_000_00L - 40_000_00L);
    }

    [Fact]
    public async Task Projecao_acumula_mes_a_mes_e_bate_com_a_soma_manual()
    {
        // Arrange
        var mesPassado = MesAtual.AddMonths(-1);
        var proximo = MesAtual.AddMonths(1);

        _caixa.Arrecadado(Arg.Any<CancellationToken>()).Returns(10_000_00L);
        _caixa.Gasto(Arg.Any<CancellationToken>()).Returns(4_000_00L);
        _caixa.ParcelasEmAberto(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns((7_000_00L, 1_500_00L));
        _caixa.EntradasPorMes(Arg.Any<CancellationToken>()).Returns([new SomaDoMes(mesPassado, 6_000_00L), new SomaDoMes(MesAtual, 4_000_00L)]);
        _caixa.SaidasPorMes(Arg.Any<CancellationToken>()).Returns([new SomaDoMes(mesPassado, 4_000_00L)]);
        _caixa.EntradasPrevistasPorMes(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([new SomaDoMes(proximo, 7_000_00L)]);
        _caixa.SaidasPrevistasPorMes(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([new SomaDoMes(proximo, 3_000_00L)]);
        _formaturas.ObterDetalhe(FormaturaId, Arg.Any<CancellationToken>()).Returns(Formatura(proximo));

        // Act
        var projecao = (await Servico.Projecao(FormaturaId, Ct)).Valor;

        // Assert
        projecao.Meses.Count.ShouldBe(3);
        projecao.Meses[0].SaldoAcumuladoEmCentavos.ShouldBe(2_000_00L);
        projecao.Meses[1].SaldoAcumuladoEmCentavos.ShouldBe(6_000_00L);
        projecao.Meses[2].SaldoAcumuladoEmCentavos.ShouldBe(10_000_00L);
        projecao.SaldoEmCentavos.ShouldBe(6_000_00L);
        projecao.EmAtrasoEmCentavos.ShouldBe(1_500_00L);
    }

    [Fact]
    public async Task So_o_mes_futuro_vem_marcado_como_projetado()
    {
        // Arrange
        var proximo = MesAtual.AddMonths(1);
        _caixa.EntradasPorMes(Arg.Any<CancellationToken>()).Returns([new SomaDoMes(MesAtual, 1_000_00L)]);
        _caixa.EntradasPrevistasPorMes(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([new SomaDoMes(proximo, 2_000_00L)]);
        _formaturas.ObterDetalhe(FormaturaId, Arg.Any<CancellationToken>()).Returns(Formatura(proximo));

        // Act
        var projecao = (await Servico.Projecao(FormaturaId, Ct)).Valor;

        // Assert
        projecao.Meses[0].Projetado.ShouldBeFalse();
        projecao.Meses[1].Projetado.ShouldBeTrue();
    }

    [Fact]
    public async Task Turma_sem_movimento_devolve_so_o_mes_atual()
    {
        // Arrange
        _formaturas.ObterDetalhe(FormaturaId, Arg.Any<CancellationToken>()).Returns(Formatura(null));

        // Act
        var projecao = (await Servico.Projecao(FormaturaId, Ct)).Valor;

        // Assert
        projecao.Meses.ShouldHaveSingleItem().Mes.ShouldBe(MesAtual);
    }

    [Fact]
    public async Task A_colacao_estica_a_projecao_ate_o_mes_dela_sem_buraco()
    {
        // Arrange
        var colacao = MesAtual.AddMonths(5).AddDays(9);
        _formaturas.ObterDetalhe(FormaturaId, Arg.Any<CancellationToken>()).Returns(Formatura(colacao));

        // Act
        var projecao = (await Servico.Projecao(FormaturaId, Ct)).Valor;

        // Assert
        projecao.Meses.Count.ShouldBe(6);
        projecao.Meses[^1].Mes.ShouldBe(MesAtual.AddMonths(5));
    }

    private static FormaturaDetalhe Formatura(DateOnly? colacao) =>
        new(
            FormaturaId,
            "Medicina 2027",
            "UFPR",
            "Medicina",
            2027,
            1,
            colacao,
            null,
            80,
            StatusDaFormatura.Ativa,
            DateTime.UtcNow,
            DateTime.UtcNow,
            null
        );
}
