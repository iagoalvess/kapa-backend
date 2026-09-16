using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Services;
using Backend.Business.Cobrancas.Validators;
using Backend.Business.Common.Datas;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>As regras do plano que dependem de estado: item em uso, repactuação, um vigente só.</summary>
public sealed class PlanoDeCobrancaServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IPlanoDeCobrancaRepository _planos = Substitute.For<IPlanoDeCobrancaRepository>();
    private readonly IParcelaRepository _parcelas = Substitute.For<IParcelaRepository>();
    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly PlanoDeCobranca _plano = new() { Nome = "Plano 2027" };
    private readonly ItemDeCobranca _mensalidade;

    public PlanoDeCobrancaServiceTests()
    {
        _mensalidade = ItemDeCobranca.Novo(_plano.Id, Mensalidade());
        _plano.Itens.Add(_mensalidade);

        _planos.ObterParaEdicao(_plano.Id, Arg.Any<CancellationToken>()).Returns(_plano);
        _planos.Obter(_plano.Id, Arg.Any<CancellationToken>()).Returns(_plano);
        _parcelas.ListarItensEmUso(_plano.Id, Arg.Any<CancellationToken>()).Returns([]);
    }

    private PlanoDeCobrancaService Servico =>
        new(
            _planos,
            _parcelas,
            _vinculos,
            new DadosDoPlanoValidator(),
            new DadosDoItemValidator(),
            new SimularPlanoValidator(),
            _unitOfWork,
            NullLogger<PlanoDeCobrancaService>.Instance
        );

    private static DadosDoItem Mensalidade(long valor = 840_000) => new(TipoDeCobranca.Mensalidade, null, valor, 24, 10, new DateOnly(2026, 3, 1));

    private void EmUso() => _parcelas.ExisteDoItem(_mensalidade.Id, Arg.Any<CancellationToken>()).Returns(true);

    [Fact]
    public async Task Remover_item_em_uso_devolve_conflito_sem_remover()
    {
        // Arrange
        EmUso();

        // Act
        var resultado = await Servico.RemoverItem(_plano.Id, _mensalidade.Id, Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.item_em_uso");
        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Conflito);
        _planos.DidNotReceiveWithAnyArgs().RemoverItem(default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Remover_item_sem_parcela_tira_do_plano()
    {
        // Act
        var resultado = await Servico.RemoverItem(_plano.Id, _mensalidade.Id, Ct);

        // Assert
        resultado.Valor.Itens.ShouldBeEmpty();
        _planos.Received(1).RemoverItem(_mensalidade);
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    [Fact]
    public async Task Item_em_uso_nao_muda_o_numero_de_parcelas()
    {
        // Arrange
        EmUso();

        // Act
        var resultado = await Servico.AlterarItem(_plano.Id, _mensalidade.Id, Mensalidade() with { NumeroDeParcelas = 12 }, Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.item_em_uso");
        _mensalidade.NumeroDeParcelas.ShouldBe(24);
    }

    /// <summary>
    /// O repositório só entrega as abertas que ainda não venceram; cada uma recebe o valor da mesma
    /// posição na grade nova — a primeira, com o resto da divisão.
    /// </summary>
    [Fact]
    public async Task Alterar_valor_de_item_em_uso_repactua_as_parcelas_futuras_pela_grade_nova()
    {
        // Arrange
        EmUso();
        var hoje = DataUtils.Hoje();
        var primeira = Parcela.Nova(Guid.CreateVersion7(), _mensalidade.Id, new ParcelaPrevista(1, hoje, 35_000));
        var decima = Parcela.Nova(Guid.CreateVersion7(), _mensalidade.Id, new ParcelaPrevista(10, hoje.AddMonths(1), 35_000));
        _parcelas.ListarAbertasParaEdicao(_mensalidade.Id, hoje, Arg.Any<CancellationToken>()).Returns([primeira, decima]);

        // Act
        var resultado = await Servico.AlterarItem(_plano.Id, _mensalidade.Id, Mensalidade(valor: 960_010), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        primeira.ValorOriginalEmCentavos.ShouldBe(40_010);
        decima.ValorOriginalEmCentavos.ShouldBe(40_000);
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    [Fact]
    public async Task Alterar_item_sem_parcela_nao_consulta_parcelas_para_repactuar()
    {
        // Act
        await Servico.AlterarItem(_plano.Id, _mensalidade.Id, Mensalidade() with { NumeroDeParcelas = 12 }, Ct);

        // Assert
        _mensalidade.NumeroDeParcelas.ShouldBe(12);
        await _parcelas.DidNotReceiveWithAnyArgs().ListarAbertasParaEdicao(default, default, Ct);
    }

    [Fact]
    public async Task Encerrar_cancela_so_o_que_vence_de_amanha_em_diante()
    {
        // Arrange
        var hoje = DataUtils.Hoje();
        var futura = Parcela.Nova(Guid.CreateVersion7(), _mensalidade.Id, new ParcelaPrevista(2, hoje.AddDays(1), 35_000));
        _parcelas.ListarAbertasParaEdicao(_mensalidade.Id, hoje.AddDays(1), Arg.Any<CancellationToken>()).Returns([futura]);

        // Act
        var resultado = await Servico.EncerrarItem(_plano.Id, _mensalidade.Id, Ct);

        // Assert
        resultado.Valor.Itens.ShouldHaveSingleItem().EncerradoEm.ShouldBe(hoje);
        futura.Status.ShouldBe(StatusDaParcela.Cancelada);
    }

    [Fact]
    public async Task Segunda_adesao_devolve_conflito()
    {
        // Arrange
        _plano.Itens.Add(ItemDeCobranca.Novo(_plano.Id, Mensalidade(20_000) with { Tipo = TipoDeCobranca.Adesao, NumeroDeParcelas = 1 }));

        // Act
        var resultado = await Servico.AdicionarItem(_plano.Id, Mensalidade(20_000) with { Tipo = TipoDeCobranca.Adesao, NumeroDeParcelas = 1 }, Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.adesao_duplicada");
        await _planos.DidNotReceiveWithAnyArgs().AdicionarItem(default!, Ct);
    }

    [Fact]
    public async Task Item_invalido_devolve_os_erros_de_forma_juntos()
    {
        // Act
        var resultado = await Servico.AdicionarItem(_plano.Id, Mensalidade(0) with { NumeroDeParcelas = 0, DiaDeVencimento = 32 }, Ct);

        // Assert
        resultado.Erros.Select(erro => erro.Campo).ShouldBe(["valor_em_centavos", "numero_de_parcelas", "dia_de_vencimento"], ignoreOrder: true);
    }

    [Fact]
    public async Task Nao_vigora_com_outro_plano_vigente()
    {
        // Arrange
        _planos.ExisteVigente(Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var resultado = await Servico.Vigorar(_plano.Id, Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.plano_vigente_existente");
        _plano.Status.ShouldBe(StatusDoPlano.Rascunho);
    }

    [Fact]
    public async Task Simulacao_sem_itens_usa_os_gravados_e_multiplica_pelos_membros_ativos()
    {
        // Arrange
        var formaturaId = Guid.CreateVersion7();
        _vinculos
            .ContarMembros(formaturaId, Arg.Any<CancellationToken>())
            .Returns([
                new ContagemDeMembros("Formando", true, 78),
                new ContagemDeMembros("Presidente", true, 2),
                new ContagemDeMembros("Formando", false, 5),
            ]);

        // Act
        var resultado = await Servico.Simular(formaturaId, _plano.Id, new SimularPlano(null), Ct);

        // Assert
        resultado.Valor.Parcelas.Count.ShouldBe(24);
        resultado.Valor.TotalPorFormando.ShouldBe(840_000);
        resultado.Valor.Formandos.ShouldBe(80);
        resultado.Valor.TotalDaTurma.ShouldBe(67_200_000);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Simulacao_do_formulario_ordena_as_parcelas_de_todos_os_itens_por_vencimento()
    {
        // Arrange
        _vinculos.ContarMembros(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        var adesao = new DadosDoItem(TipoDeCobranca.Adesao, " ", 50_000, 1, 5, new DateOnly(2026, 4, 1));

        // Act
        var resultado = await Servico.Simular(Guid.CreateVersion7(), _plano.Id, new SimularPlano([Mensalidade(), adesao]), Ct);

        // Assert
        resultado.Valor.Parcelas.Select(parcela => parcela.Vencimento).ShouldBeInOrder();
        var parcelaDaAdesao = resultado.Valor.Parcelas.Single(parcela => parcela.Tipo == TipoDeCobranca.Adesao);
        parcelaDaAdesao.Descricao.ShouldBeNull();
        parcelaDaAdesao.De.ShouldBe(1);
        resultado.Valor.TotalPorFormando.ShouldBe(890_000);
    }
}
