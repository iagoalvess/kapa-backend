using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Services;
using Backend.Business.Cobrancas.Validators;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>As regras do plano que dependem de estado: item em uso, repactuação, um vigente só.</summary>
public sealed class PlanoDeCobrancaServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Quem executou; vai no evento de auditoria de cada operação.</summary>
    private static readonly Guid Autor = Guid.CreateVersion7();

    private readonly IPlanoDeCobrancaRepository _planos = Substitute.For<IPlanoDeCobrancaRepository>();
    private readonly IParcelaRepository _parcelas = Substitute.For<IParcelaRepository>();
    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly IGeracaoDeParcelasService _geracao = Substitute.For<IGeracaoDeParcelasService>();
    private readonly IEventoRepository _eventos = Substitute.For<IEventoRepository>();
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
        _geracao.GerarDoItem(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<ItemDeCobranca>(), Arg.Any<CancellationToken>()).Returns(0);
    }

    private PlanoDeCobrancaService Servico =>
        new(
            _planos,
            _parcelas,
            _vinculos,
            _geracao,
            new DadosDoPlanoValidator(),
            new DadosDoItemValidator(),
            new DadosDoPacoteValidator(),
            new RateioExtraordinarioValidator(),
            new SimularPlanoValidator(),
            _eventos,
            new ValoresADevolver(Substitute.For<IValorADevolverRepository>()),
            _unitOfWork,
            NullLogger<PlanoDeCobrancaService>.Instance
        );

    private static DadosDoItem Mensalidade(long valor = 840_000) => new(TipoDeCobranca.Mensalidade, null, valor, 24, 10, new DateOnly(2026, 3, 1));

    /// <summary>O item do rateio: uma avulsa de R$ 100 no mês que vem — "o buffet subiu".</summary>
    private static DadosDoItem Rateavel() => new(TipoDeCobranca.Avulsa, "Rateio do buffet", 10_000, 1, 10, DataUtils.Hoje().AddMonths(1));

    private void EmUso() => _parcelas.ExisteDoItem(_mensalidade.Id, Arg.Any<CancellationToken>()).Returns(true);

    [Fact]
    public async Task Remover_item_em_uso_devolve_conflito_sem_remover()
    {
        // Arrange
        EmUso();

        // Act
        var resultado = await Servico.RemoverItem(_plano.Id, _mensalidade.Id, Autor, Ct);

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
        var resultado = await Servico.RemoverItem(_plano.Id, _mensalidade.Id, Autor, Ct);

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
        var resultado = await Servico.AlterarItem(
            _plano.Id,
            _mensalidade.Id,
            new DadosDoPacote(Mensalidade() with { NumeroDeParcelas = 12 }),
            Autor,
            ct: Ct
        );

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.item_em_uso");
        _mensalidade.NumeroDeParcelas.ShouldBe(24);
    }

    /// <summary>
    /// O que já venceu fica como está; a mudança de preço se redistribui pelo que ainda não venceu, com o resto na
    /// primeira.
    /// </summary>
    [Fact]
    public async Task Alterar_valor_de_item_em_uso_redistribui_o_que_falta_pelas_parcelas_futuras()
    {
        // Arrange
        EmUso();
        var hoje = DataUtils.Hoje();
        var vinculoId = Guid.CreateVersion7();
        var vencida = Parcela.Nova(vinculoId, _mensalidade.Id, new ParcelaPrevista(1, hoje.AddMonths(-1), 35_000));
        var primeiraFutura = Parcela.Nova(vinculoId, _mensalidade.Id, new ParcelaPrevista(2, hoje, 35_000));
        var segundaFutura = Parcela.Nova(vinculoId, _mensalidade.Id, new ParcelaPrevista(3, hoje.AddMonths(1), 35_000));
        _parcelas.ListarDoItemParaEdicao(_mensalidade.Id, Arg.Any<CancellationToken>()).Returns([vencida, primeiraFutura, segundaFutura]);

        // Act
        var resultado = await Servico.AlterarItem(
            _plano.Id,
            _mensalidade.Id,
            new DadosDoPacote(Mensalidade(valor: 850_001)),
            Autor,
            aplicarAosAtuais: true,
            Ct
        );

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        vencida.ValorOriginalEmCentavos.ShouldBe(35_000);
        primeiraFutura.ValorOriginalEmCentavos.ShouldBe(40_001);
        segundaFutura.ValorOriginalEmCentavos.ShouldBe(40_000);
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    /// <summary>
    /// Quem aderiu depois tem menos parcelas que o item, e recebe o mesmo aumento que a turma: a redistribuição é por
    /// formando, não pela grade do item.
    /// </summary>
    [Fact]
    public async Task Repactuacao_de_quem_aderiu_depois_nao_o_deixa_pagando_menos()
    {
        // Arrange
        EmUso();
        var hoje = DataUtils.Hoje();
        var cedo = Guid.CreateVersion7();
        var tarde = Guid.CreateVersion7();
        var doCedo = Parcela.Nova(cedo, _mensalidade.Id, new ParcelaPrevista(1, hoje, 35_000));
        var doTarde = Parcela.Nova(tarde, _mensalidade.Id, new ParcelaPrevista(24, hoje.AddMonths(2), 35_000));
        _parcelas.ListarDoItemParaEdicao(_mensalidade.Id, Arg.Any<CancellationToken>()).Returns([doCedo, doTarde]);

        // Act
        await Servico.AlterarItem(_plano.Id, _mensalidade.Id, new DadosDoPacote(Mensalidade(valor: 905_000)), Autor, aplicarAosAtuais: true, Ct);

        // Assert
        doCedo.ValorOriginalEmCentavos.ShouldBe(100_000);
        doTarde.ValorOriginalEmCentavos.ShouldBe(100_000);
    }

    /// <summary>
    /// Sprint 48, D21: o preço novo vale para quem aderir depois; quem já aderiu fica com o contrato, salvo se a
    /// tesouraria marcar.
    /// </summary>
    [Fact]
    public async Task Alterar_valor_sem_marcar_nao_repactua_quem_ja_aderiu()
    {
        // Arrange
        EmUso();
        var futura = Parcela.Nova(Guid.CreateVersion7(), _mensalidade.Id, new ParcelaPrevista(1, DataUtils.Hoje().AddMonths(1), 35_000));
        _parcelas.ListarDoItemParaEdicao(_mensalidade.Id, Arg.Any<CancellationToken>()).Returns([futura]);

        // Act
        var resultado = await Servico.AlterarItem(_plano.Id, _mensalidade.Id, new DadosDoPacote(Mensalidade(valor: 900_000)), Autor, ct: Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        _mensalidade.ValorEmCentavos.ShouldBe(900_000);
        futura.ValorOriginalEmCentavos.ShouldBe(35_000);
    }

    /// <summary>
    /// Quem subiu de faixa pelo aditivo deve neste item só a diferença; a repactuação soma a mudança de preço ao que ele
    /// devia, e não o leva ao preço cheio (Sprint 48, D38).
    /// </summary>
    [Fact]
    public async Task Repactuacao_soma_o_delta_ao_que_cada_um_devia()
    {
        // Arrange
        EmUso();
        var hoje = DataUtils.Hoje();
        var doAditivo = Parcela.Nova(Guid.CreateVersion7(), _mensalidade.Id, new ParcelaPrevista(1, hoje.AddMonths(1), 60_000));
        _parcelas.ListarDoItemParaEdicao(_mensalidade.Id, Arg.Any<CancellationToken>()).Returns([doAditivo]);

        // Act
        await Servico.AlterarItem(_plano.Id, _mensalidade.Id, new DadosDoPacote(Mensalidade(valor: 850_000)), Autor, aplicarAosAtuais: true, Ct);

        // Assert
        doAditivo.ValorOriginalEmCentavos.ShouldBe(70_000);
    }

    /// <summary>A pergunta do D21 mostra quantos e quanto, sem mexer em nada.</summary>
    [Fact]
    public async Task Simular_preco_conta_formandos_e_total_sem_alterar()
    {
        // Arrange
        var hoje = DataUtils.Hoje();
        var ana = Parcela.Nova(Guid.CreateVersion7(), _mensalidade.Id, new ParcelaPrevista(1, hoje.AddMonths(1), 35_000));
        var bruno = Parcela.Nova(Guid.CreateVersion7(), _mensalidade.Id, new ParcelaPrevista(1, hoje.AddMonths(1), 35_000));
        var paga = Parcela.Nova(Guid.CreateVersion7(), _mensalidade.Id, new ParcelaPrevista(1, hoje.AddMonths(-1), 35_000));
        _parcelas.ListarDoItemParaEdicao(_mensalidade.Id, Arg.Any<CancellationToken>()).Returns([ana, bruno, paga]);

        // Act
        var resultado = await Servico.SimularPreco(_plano.Id, _mensalidade.Id, 850_000, Ct);

        // Assert
        resultado.Valor.ShouldBe(new Alcance(2, 2, 20_000));
        ana.ValorOriginalEmCentavos.ShouldBe(35_000);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Alterar_item_sem_parcela_nao_consulta_parcelas_para_repactuar()
    {
        // Act
        await Servico.AlterarItem(_plano.Id, _mensalidade.Id, new DadosDoPacote(Mensalidade() with { NumeroDeParcelas = 12 }), Autor, ct: Ct);

        // Assert
        _mensalidade.NumeroDeParcelas.ShouldBe(12);
        await _parcelas.DidNotReceiveWithAnyArgs().ListarDoItemParaEdicao(default, Ct);
    }

    [Fact]
    public async Task Encerrar_cancela_so_o_que_vence_de_amanha_em_diante()
    {
        // Arrange
        var hoje = DataUtils.Hoje();
        var futura = Parcela.Nova(Guid.CreateVersion7(), _mensalidade.Id, new ParcelaPrevista(2, hoje.AddDays(1), 35_000));
        _parcelas.ListarAbertasParaEdicao(_mensalidade.Id, hoje.AddDays(1), Arg.Any<CancellationToken>()).Returns([futura]);

        // Act
        var resultado = await Servico.EncerrarItem(_plano.Id, _mensalidade.Id, Autor, Ct);

        // Assert
        resultado.Valor.Itens.ShouldHaveSingleItem().EncerradoEm.ShouldBe(hoje);
        futura.Status.ShouldBe(StatusDaParcela.Cancelada);
    }

    /// <summary>Quem já escolheu o pacote tem os convites emitidos por estes números (Sprint 47).</summary>
    [Fact]
    public async Task Pacote_em_uso_nao_muda_os_beneficios()
    {
        // Arrange
        EmUso();

        // Act
        var resultado = await Servico.AlterarItem(_plano.Id, _mensalidade.Id, new DadosDoPacote(Mensalidade(), ConvitesDaFesta: 5), Autor, ct: Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.item_em_uso");
        _mensalidade.ConvitesDaFesta.ShouldBe(0);
    }

    /// <summary>A última parcela do teto não passa do último vencimento que a comissão definiu (D28).</summary>
    [Fact]
    public async Task Pacote_cuja_grade_passa_do_ultimo_vencimento_e_recusado()
    {
        // Arrange
        var dados = new DadosDoPacote(Mensalidade() with { Tipo = TipoDeCobranca.Festa }, "Festa", 10, UltimoVencimento: new DateOnly(2027, 1, 31));

        // Act
        var resultado = await Servico.AdicionarItem(_plano.Id, dados, ct: Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.ultima_parcela_depois_do_limite");
        await _planos.DidNotReceiveWithAnyArgs().AdicionarItem(default!, Ct);
    }

    [Fact]
    public async Task Item_invalido_devolve_os_erros_de_forma_juntos()
    {
        // Act
        var resultado = await Servico.AdicionarItem(
            _plano.Id,
            new DadosDoPacote(Mensalidade(0) with { NumeroDeParcelas = 0, DiaDeVencimento = 32 }),
            ct: Ct
        );

        // Assert
        resultado.Erros.Select(erro => erro.Campo).ShouldBe(["valor_em_centavos", "numero_de_parcelas", "dia_de_vencimento"], ignoreOrder: true);
    }

    [Fact]
    public async Task Item_comum_nao_alcanca_quem_ja_aderiu()
    {
        // Act
        await Servico.AdicionarItem(_plano.Id, new DadosDoPacote(Mensalidade() with { Tipo = TipoDeCobranca.FotoEAlbum }), ct: Ct);

        // Assert
        await _geracao.DidNotReceiveWithAnyArgs().GerarDoItem(default!, default!, Ct);
        await _parcelas.DidNotReceiveWithAnyArgs().ListarVinculosAtivosComParcela(default, default, Ct);
    }

    [Fact]
    public async Task Rateio_grava_a_grade_para_os_vinculos_ativos_que_ja_aderiram()
    {
        // Arrange
        Guid[] aderentes = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        _parcelas.ListarVinculosAtivosComParcela(_plano.Id, Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>()).Returns(aderentes);
        _geracao.GerarDoItem(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<ItemDeCobranca>(), Arg.Any<CancellationToken>()).Returns(2);

        // Act
        var resultado = await Servico.AdicionarItem(_plano.Id, new DadosDoPacote(Rateavel()), new RateioExtraordinario(" assembleia de 12/10 "), Ct);

        // Assert
        var incluido = resultado.Valor.Itens.Single(item => item.ValorEmCentavos == 10_000);
        incluido.OrigemDaDecisao.ShouldBe("assembleia de 12/10");
        await _geracao.Received(1).GerarDoItem(aderentes, Arg.Is<ItemDeCobranca>(item => item.Id == incluido.Id), Ct);
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    /// <summary>D19: o custo da festa só alcança quem tem a festa na cesta — o alvo vai à consulta e fica no item.</summary>
    [Fact]
    public async Task Rateio_com_alvo_so_alcanca_quem_tem_o_pacote()
    {
        // Arrange
        var festa = ItemDeCobranca.NovoPacote(_plano.Id, new DadosDoPacote(Mensalidade() with { Tipo = TipoDeCobranca.Festa }, "Festa"));
        _plano.Itens.Add(festa);
        Guid[] daFesta = [Guid.CreateVersion7()];
        _parcelas
            .ListarVinculosAtivosComParcela(
                _plano.Id,
                Arg.Is<IReadOnlyCollection<Guid>?>(alvo => alvo!.Single() == festa.Id),
                Arg.Any<CancellationToken>()
            )
            .Returns(daFesta);

        // Act
        var resultado = await Servico.AdicionarItem(_plano.Id, new DadosDoPacote(Rateavel()), new RateioExtraordinario("assembleia", [festa.Id]), Ct);

        // Assert
        resultado.Valor.Itens.Single(item => item.ValorEmCentavos == 10_000).AlvoDoRateio.ShouldBe([festa.Id]);
        await _geracao.Received(1).GerarDoItem(daFesta, Arg.Any<ItemDeCobranca>(), Ct);
    }

    [Fact]
    public async Task Rateio_com_alvo_fora_do_catalogo_e_recusado()
    {
        // Act
        var resultado = await Servico.AdicionarItem(
            _plano.Id,
            new DadosDoPacote(Rateavel()),
            new RateioExtraordinario("assembleia", [Guid.CreateVersion7()]),
            Ct
        );

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.alvo_invalido");
        await _geracao.DidNotReceiveWithAnyArgs().GerarDoItem(default!, default!, Ct);
    }

    [Fact]
    public async Task Rateio_sem_origem_nao_cobra_ninguem()
    {
        // Act
        var resultado = await Servico.AdicionarItem(_plano.Id, new DadosDoPacote(Rateavel()), new RateioExtraordinario("  "), Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.origem_obrigatoria");
        await _geracao.DidNotReceiveWithAnyArgs().GerarDoItem(default!, default!, Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Rateio_com_primeiro_mes_no_passado_nao_nasce_vencido()
    {
        // Act
        var resultado = await Servico.AdicionarItem(
            _plano.Id,
            new DadosDoPacote(Rateavel() with { PrimeiroMes = DataUtils.Hoje().AddMonths(-1) }),
            new RateioExtraordinario("assembleia de 12/10"),
            Ct
        );

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.rateio_retroativo");
        resultado.PrimeiroErro.Campo.ShouldBe("primeiro_mes");
        await _geracao.DidNotReceiveWithAnyArgs().GerarDoItem(default!, default!, Ct);
    }

    [Fact]
    public async Task Rateio_no_mes_corrente_passa()
    {
        // Act
        var resultado = await Servico.AdicionarItem(
            _plano.Id,
            new DadosDoPacote(Rateavel() with { PrimeiroMes = DataUtils.Hoje() }),
            new RateioExtraordinario("assembleia de 12/10"),
            Ct
        );

        // Assert
        resultado.Sucesso.ShouldBeTrue();
    }

    [Fact]
    public async Task Nao_vigora_com_outro_plano_vigente()
    {
        // Arrange
        _planos.ExisteVigente(Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var resultado = await Servico.Vigorar(_plano.Id, Autor, Ct);

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
                new ContagemDeMembros("Formando", true, false, false, 78),
                new ContagemDeMembros("Presidente", true, false, false, 2),
                new ContagemDeMembros("Formando", false, true, false, 5),
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
