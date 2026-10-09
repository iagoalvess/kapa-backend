using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Relatorios.Interfaces;
using Backend.Business.Relatorios.Models;
using Backend.Business.Relatorios.Services;
using Backend.Business.Usuarios.Interfaces;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Relatorios;

/// <summary>A exportação valida registros do recorte, nunca saldo ou tamanho do arquivo.</summary>
public sealed class RelatorioServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid FormaturaId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();
    private static readonly FiltroDoRelatorio Filtro = new(new(new(2026, 1, 1), new(2026, 10, 9)));
    private readonly IRelatorioRepository _relatorios = Substitute.For<IRelatorioRepository>();
    private readonly ISolicitacaoDeRelatorioRepository _solicitacoes = Substitute.For<ISolicitacaoDeRelatorioRepository>();
    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public RelatorioServiceTests()
    {
        _formaturas
            .ObterDetalheDeTodasAsFormaturas(FormaturaId, Ct)
            .Returns(new FormaturaDetalhe(FormaturaId, "Turma", "UFPR", "Medicina", 2027, 1, null, null, StatusDaFormatura.Ativa, null, true));
        _relatorios.EntradasPorTipo(Arg.Any<PeriodoDoRelatorio>(), Ct).Returns([]);
        _relatorios.OutrasReceitasPorCategoria(Arg.Any<PeriodoDoRelatorio>(), Ct).Returns([]);
        _relatorios.SaidasPorCategoria(Arg.Any<PeriodoDoRelatorio>(), Ct).Returns([]);
        _relatorios.PorFornecedor(Arg.Any<PeriodoDoRelatorio?>(), Ct).Returns([]);
        _relatorios.ListarDespesas(Arg.Any<FiltroDoRelatorio>(), Ct).Returns(Fluxo<DespesaExportada>());
        _relatorios.ListarParcelas(Arg.Any<FiltroDoRelatorio>(), Ct).Returns(Fluxo<ParcelaExportada>());
    }

    private RelatorioService Servico =>
        new(
            _relatorios,
            _solicitacoes,
            Substitute.For<ICaixaService>(),
            _formaturas,
            Substitute.For<IUsuarioRepository>(),
            Substitute.For<IArquivoService>(),
            _unitOfWork
        );

    [Theory]
    [InlineData(TipoDeRelatorio.Balancete)]
    [InlineData(TipoDeRelatorio.Despesas)]
    [InlineData(TipoDeRelatorio.Parcelas)]
    [InlineData(TipoDeRelatorio.Fornecedores)]
    public async Task Sem_registros_nao_gera_excel_nem_grava_pedido_de_pdf(TipoDeRelatorio tipo)
    {
        var planilha = await Servico.Planilha(FormaturaId, tipo, Filtro, Ct);
        var pdf = await Servico.Solicitar(FormaturaId, tipo, Filtro, UsuarioId, Ct);

        planilha.PrimeiroErro.Codigo.ShouldBe("relatorio.sem_dados");
        pdf.PrimeiroErro.Codigo.ShouldBe("relatorio.sem_dados");
        await _solicitacoes.DidNotReceive().Adicionar(Arg.Any<SolicitacaoDeRelatorio>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Balancete_com_registros_de_valor_zero_continua_exportavel()
    {
        _relatorios.EntradasPorTipo(Filtro.Periodo, Ct).Returns([new LinhaDeBalancete("Mensalidade", 1, 0)]);

        var tabela = await Servico.Tabela(FormaturaId, TipoDeRelatorio.Balancete, Filtro, Ct);

        tabela.Sucesso.ShouldBeTrue();
        tabela.Valor.Linhas.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Situacao_da_parcela_e_aplicada_antes_de_decidir_se_ha_dados()
    {
        var parcela = new ParcelaExportada("Ana", "Mensalidade", 1, 12, new(2026, 1, 10), 35000, StatusDaParcela.Paga, 35000, new(2026, 1, 10));
        _relatorios.ListarParcelas(Arg.Any<FiltroDoRelatorio>(), Ct).Returns(Fluxo(parcela));

        var filtrada = await Servico.Tabela(FormaturaId, TipoDeRelatorio.Parcelas, Filtro with { SituacaoDaParcela = StatusDaParcela.Aberta }, Ct);
        var completa = await Servico.Planilha(FormaturaId, TipoDeRelatorio.Parcelas, Filtro, Ct);

        filtrada.PrimeiroErro.Codigo.ShouldBe("relatorio.sem_dados");
        completa.Sucesso.ShouldBeTrue();
        completa.Valor.Conteudo.Dispose();
    }

    private static async IAsyncEnumerable<T> Fluxo<T>(params T[] linhas)
    {
        foreach (var linha in linhas)
            yield return linha;
        await Task.CompletedTask;
    }
}
