using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Services;
using Backend.Business.Assinaturas.Settings;
using Backend.Business.Assinaturas.Validators;
using Backend.Business.Common;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Assinaturas;

/// <summary>
/// Checkout e cancelamento: o que depende do status da formatura e da resposta do provedor.
/// </summary>
public sealed class AssinaturaServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IAssinaturaRepository _assinaturas = Substitute.For<IAssinaturaRepository>();
    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();
    private readonly IProvedorDeAssinatura _provedor = Substitute.For<IProvedorDeAssinatura>();
    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static readonly Plano Premium = new()
    {
        Codigo = "premium",
        Nome = "Premium",
        PrecoEmCentavos = 4990,
        LimiteDeFormandos = 400,
    };

    public AssinaturaServiceTests()
    {
        _assinaturas.ObterPlanoAtivo("premium", Arg.Any<CancellationToken>()).Returns(Premium);
        _provedor
            .CriarCheckout(Arg.Any<PedidoDeCheckout>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new SessaoDeCheckout("sessao-1", "https://psp/checkout/sessao-1")));
    }

    private AssinaturaService Servico =>
        new(
            _assinaturas,
            _formaturas,
            _provedor,
            new VagasDoPlano(_assinaturas, _vinculos),
            new IniciarCheckoutValidator(),
            new TrocaDePlanoValidator(),
            Options.Create(new AssinaturaSettings()),
            Options.Create(new AplicacaoSettings { UrlDoFrontend = "https://app.kapa" }),
            _unitOfWork
        );

    private Formatura FormaturaEm(StatusDaFormatura status)
    {
        var formatura = new Formatura();
        formatura.NascerNoGratuito();

        if (status != StatusDaFormatura.Ativa)
            formatura.Transicionar(status);

        _formaturas.ObterParaEdicao(formatura.Id, Arg.Any<CancellationToken>()).Returns(formatura);

        return formatura;
    }

    [Fact]
    public async Task Checkout_cria_assinatura_pendente_sem_mexer_no_status_da_turma()
    {
        // Arrange
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);
        Assinatura? criada = null;
        await _assinaturas.Adicionar(Arg.Do<Assinatura>(a => criada = a), Arg.Any<CancellationToken>());

        // Act
        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("premium"), Ct);

        // Assert
        resultado.Valor.Url.ShouldBe("https://psp/checkout/sessao-1");
        criada.ShouldNotBeNull();
        criada.Status.ShouldBe(StatusDaAssinatura.Pendente);
        criada.IdExterno.ShouldBe("sessao-1");
        formatura.Status.ShouldBe(StatusDaFormatura.Ativa);
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>R$ 49,90 sai do catálogo e chega ao provedor como 4990, sem passar por decimal.</summary>
    [Fact]
    public async Task Valor_chega_ao_provedor_em_centavos_exatos()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);

        await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("premium"), Ct);

        await _provedor
            .Received(1)
            .CriarCheckout(
                Arg.Is<PedidoDeCheckout>(p => p.PrecoEmCentavos == 4990 && p.UrlDeRetorno == "https://app.kapa/assinatura/retorno"),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>Quem já tem assinatura ativa não abre outro checkout — a turma estar ativa não basta.</summary>
    [Fact]
    public async Task Checkout_com_assinatura_ativa_devolve_ja_ativa_sem_chamar_o_provedor()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);
        var ativa = new Assinatura();
        ativa.ConfirmarPagamento(DateTime.UtcNow, CicloDeCobranca.Mensal);
        _assinaturas.ObterMaisRecenteParaEdicao(Arg.Any<CancellationToken>()).Returns(ativa);

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("premium"), Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("assinatura.ja_ativa");
        await _provedor.DidNotReceiveWithAnyArgs().CriarCheckout(default!, Ct);
    }

    /// <summary>
    /// Plano menor que a turma é recusado: sem isso, a turma de 200 no Premium deixava vencer e
    /// contratava o Essencial com os 200 dentro.
    /// </summary>
    /// <param name="ocupadas">Vagas já ocupadas.</param>
    /// <param name="passa">Se o checkout sai.</param>
    [Theory]
    [InlineData(401, false)]
    [InlineData(400, true)]
    public async Task Checkout_de_plano_menor_que_a_turma_e_recusado(int ocupadas, bool passa)
    {
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);
        _vinculos
            .ContarMembros(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([new ContagemDeMembros(PapelNaFormatura.Formando, true, false, false, ocupadas)]);

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("premium"), Ct);

        resultado.Sucesso.ShouldBe(passa);
        if (!passa)
        {
            resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("assinatura.plano_menor_que_a_turma");
            await _provedor.DidNotReceiveWithAnyArgs().CriarCheckout(default!, Ct);
        }
    }

    /// <summary>Provedor fora do ar: 503, nada gravado, e a formatura continua em rascunho.</summary>
    [Fact]
    public async Task Provedor_fora_do_ar_nao_grava_nada()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);
        _provedor
            .CriarCheckout(Arg.Any<PedidoDeCheckout>(), Arg.Any<CancellationToken>())
            .Returns(Result.Falha<SessaoDeCheckout>(Erro.Indisponivel("assinatura.provedor_fora_do_ar", "Fora do ar.")));

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("premium"), Ct);

        resultado.Erros.ShouldHaveSingleItem().Tipo.ShouldBe(ETipoErro.Indisponivel);
        formatura.Status.ShouldBe(StatusDaFormatura.Ativa);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
        await _assinaturas.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>Fechou a aba e clicou de novo: retoma a pendente em vez de empilhar outra.</summary>
    [Fact]
    public async Task Checkout_repetido_retoma_a_pendente()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);
        var pendente = new Assinatura();
        _assinaturas.ObterMaisRecenteParaEdicao(Arg.Any<CancellationToken>()).Returns(pendente);

        await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("premium"), Ct);

        await _assinaturas.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
        await _provedor.Received(1).CriarCheckout(Arg.Is<PedidoDeCheckout>(p => p.AssinaturaId == pendente.Id), Arg.Any<CancellationToken>());
        pendente.IdExterno.ShouldBe("sessao-1");
    }

    /// <summary>
    /// Pediu o Essencial, depois o Premium: a sessão do Essencial morre no provedor antes da troca,
    /// senão pagá-la ativaria o Premium pelo preço do Essencial.
    /// </summary>
    [Fact]
    public async Task Trocar_de_plano_invalida_a_sessao_anterior_no_provedor()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);
        var pendente = new Assinatura { PlanoId = Guid.CreateVersion7(), IdExterno = "sessao-essencial" };
        _assinaturas.ObterMaisRecenteParaEdicao(Arg.Any<CancellationToken>()).Returns(pendente);
        _provedor.Cancelar("sessao-essencial", Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("premium"), Ct);

        resultado.Sucesso.ShouldBeTrue();
        await _provedor.Received(1).Cancelar("sessao-essencial", Arg.Any<CancellationToken>());
        pendente.PlanoId.ShouldBe(Premium.Id);
    }

    /// <summary>Se o provedor não expirou a sessão antiga, o plano não troca.</summary>
    [Fact]
    public async Task Sessao_anterior_que_nao_expira_mantem_o_plano()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);
        var planoAnterior = Guid.CreateVersion7();
        var pendente = new Assinatura { PlanoId = planoAnterior, IdExterno = "sessao-essencial" };
        _assinaturas.ObterMaisRecenteParaEdicao(Arg.Any<CancellationToken>()).Returns(pendente);
        _provedor
            .Cancelar("sessao-essencial", Arg.Any<CancellationToken>())
            .Returns(Result.Falha(Erro.Indisponivel("assinatura.provedor_fora_do_ar", "Fora do ar.")));

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("premium"), Ct);

        resultado.Falhou.ShouldBeTrue();
        pendente.PlanoId.ShouldBe(planoAnterior);
        await _provedor.DidNotReceiveWithAnyArgs().CriarCheckout(default!, Ct);
    }

    /// <summary>
    /// A sessão anterior do mesmo plano também cai: viva, ela pagaria a turma em dobro — e, depois de uma troca
    /// de plano, só a última seria cancelada, deixando a primeira ativar o plano novo pelo preço do antigo.
    /// </summary>
    [Fact]
    public async Task Mesmo_plano_tambem_cancela_a_sessao_anterior()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);
        _assinaturas
            .ObterMaisRecenteParaEdicao(Arg.Any<CancellationToken>())
            .Returns(new Assinatura { PlanoId = Premium.Id, IdExterno = "sessao-anterior" });
        _provedor.Cancelar("sessao-anterior", Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("premium"), Ct);

        resultado.Sucesso.ShouldBeTrue();
        await _provedor.Received(1).Cancelar("sessao-anterior", Ct);
    }

    /// <summary>Suspensa contrata, mas continua suspensa até o pagamento confirmar.</summary>
    [Fact]
    public async Task Checkout_de_suspensa_nao_muda_o_status_da_formatura()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Suspensa);

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("premium"), Ct);

        resultado.Sucesso.ShouldBeTrue();
        formatura.Status.ShouldBe(StatusDaFormatura.Suspensa);
    }

    [Fact]
    public async Task Plano_inexistente_devolve_validacao_no_campo()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("ouro"), Ct);

        resultado.Erros.ShouldHaveSingleItem().Campo.ShouldBe("plano_codigo");
    }

    [Fact]
    public async Task Cancelar_sem_assinatura_ativa_nao_chama_o_provedor()
    {
        _assinaturas.ObterMaisRecenteParaEdicao(Arg.Any<CancellationToken>()).Returns(new Assinatura { IdExterno = "sub-1" });

        var resultado = await Servico.Cancelar(Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("assinatura.nao_ativa");
        await _provedor.DidNotReceiveWithAnyArgs().Cancelar(default!, Ct);
    }

    private static readonly Plano Essencial = new()
    {
        Codigo = "essencial",
        Nome = "Essencial",
        PrecoEmCentavos = 2990,
        LimiteDeFormandos = 50,
    };

    /// <summary>Uma assinatura ativa no plano, paga há <paramref name="diasPagos"/> dias, devolvida pelo repositório.</summary>
    private Assinatura AtivaEm(Plano plano, int diasPagos = 0, MeioDePagamento meio = MeioDePagamento.Cartao, string? recorrencia = "pre_1")
    {
        var assinatura = new Assinatura
        {
            PlanoId = plano.Id,
            Meio = meio,
            IdExterno = recorrencia,
        };
        assinatura.ConfirmarPagamento(DateTime.UtcNow.AddDays(-diasPagos), plano.Ciclo);

        _assinaturas.ObterMaisRecenteParaEdicao(Arg.Any<CancellationToken>()).Returns(assinatura);
        _assinaturas.ObterPlano(plano.Id, Arg.Any<CancellationToken>()).Returns(plano);
        _assinaturas.ObterPlano(Essencial.Id, Arg.Any<CancellationToken>()).Returns(Essencial);
        _assinaturas.ObterPlano(Premium.Id, Arg.Any<CancellationToken>()).Returns(Premium);
        _assinaturas.ObterPlanoAtivo("essencial", Arg.Any<CancellationToken>()).Returns(Essencial);
        _assinaturas
            .ObterDetalheDaMaisRecente(Arg.Any<CancellationToken>())
            .Returns(new AssinaturaDetalhe(assinatura.Id, assinatura.Status, null!, null, null, null, meio, null, false));
        _provedor.Cancelar(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());
        _provedor.AtualizarValor(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());

        return assinatura;
    }

    /// <summary>Sprint 37: pelo PIX a contratação é uma cobrança aberta, com a página do provedor; não há recorrência.</summary>
    [Fact]
    public async Task Checkout_no_pix_abre_a_cobranca_do_ciclo_sem_recorrencia()
    {
        // Arrange
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);
        Assinatura? criada = null;
        CobrancaDaAssinatura? cobranca = null;
        await _assinaturas.Adicionar(Arg.Do<Assinatura>(a => criada = a), Arg.Any<CancellationToken>());
        await _assinaturas.AdicionarCobranca(Arg.Do<CobrancaDaAssinatura>(c => cobranca = c), Arg.Any<CancellationToken>());

        // Act
        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("premium", MeioDePagamento.Pix, "p@turma.com"), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        criada!.Meio.ShouldBe(MeioDePagamento.Pix);
        criada.IdExterno.ShouldBeNull();
        cobranca!.ValorEmCentavos.ShouldBe(4990);
        cobranca.Url.ShouldBe("https://psp/checkout/sessao-1");
        await _provedor
            .Received(1)
            .CriarCheckout(Arg.Is<PedidoDeCheckout>(p => p.CobrancaId == cobranca.Id && p.Meio == MeioDePagamento.Pix), Arg.Any<CancellationToken>());
    }

    /// <summary>P4: faltando metade do ciclo, a subida cobra metade da diferença — e o plano só muda quando ela for paga.</summary>
    [Fact]
    public async Task Subida_de_plano_cobra_a_diferenca_proporcional()
    {
        // Arrange
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);
        var assinatura = AtivaEm(Essencial, diasPagos: 15);
        CobrancaDaAssinatura? diferenca = null;
        await _assinaturas.AdicionarCobranca(Arg.Do<CobrancaDaAssinatura>(c => diferenca = c), Arg.Any<CancellationToken>());

        // Act
        var resultado = await Servico.TrocarPlano(formatura.Id, "premium", "p@turma.com", Ct);

        // Assert
        resultado.Valor.Url.ShouldBe("https://psp/checkout/sessao-1");
        diferenca!.Motivo.ShouldBe(MotivoDaCobranca.Diferenca);
        diferenca.PlanoId.ShouldBe(Premium.Id);
        diferenca.ValorEmCentavos.ShouldBeInRange(950, 1050);
        assinatura.PlanoId.ShouldBe(Essencial.Id);
    }

    /// <summary>P4: a descida vale na renovação, se a turma couber — e a recorrência já passa a cobrar o preço novo.</summary>
    [Fact]
    public async Task Descida_de_plano_agenda_e_ajusta_a_recorrencia()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);
        var assinatura = AtivaEm(Premium);

        var resultado = await Servico.TrocarPlano(formatura.Id, "essencial", null, Ct);

        resultado.Valor.Url.ShouldBeNull();
        assinatura.PlanoId.ShouldBe(Premium.Id);
        assinatura.PlanoDoProximoCicloId.ShouldBe(Essencial.Id);
        await _provedor.Received(1).AtualizarValor("pre_1", 2990, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Descida_para_plano_menor_que_a_turma_e_recusada()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);
        AtivaEm(Premium);
        _vinculos
            .ContarMembros(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([new ContagemDeMembros(PapelNaFormatura.Formando, true, false, false, 51)]);

        var resultado = await Servico.TrocarPlano(formatura.Id, "essencial", null, Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("assinatura.plano_menor_que_a_turma");
        await _provedor.DidNotReceiveWithAnyArgs().AtualizarValor(default!, default, Ct);
    }

    [Fact]
    public async Task Troca_entre_mensal_e_anual_e_recusada()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);
        AtivaEm(Essencial);
        _assinaturas
            .ObterPlanoAtivo("premium-anual", Arg.Any<CancellationToken>())
            .Returns(
                new Plano
                {
                    Codigo = "premium-anual",
                    PrecoEmCentavos = 47900,
                    Ciclo = CicloDeCobranca.Anual,
                    LimiteDeFormandos = 400,
                }
            );

        var resultado = await Servico.TrocarPlano(formatura.Id, "premium-anual", null, Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("assinatura.troca_de_ciclo");
    }

    /// <summary>P5: do cartão para o PIX, a recorrência é cancelada agora e o próximo ciclo vira um PIX.</summary>
    [Fact]
    public async Task Trocar_para_o_pix_cancela_a_recorrencia()
    {
        var assinatura = AtivaEm(Premium);

        var resultado = await Servico.TrocarMeio(MeioDePagamento.Pix, null, Ct);

        resultado.Valor.Url.ShouldBeNull();
        assinatura.Meio.ShouldBe(MeioDePagamento.Pix);
        assinatura.IdExterno.ShouldBeNull();
        await _provedor.Received(1).Cancelar("pre_1", Arg.Any<CancellationToken>());
    }

    /// <summary>P5: do PIX para o cartão, o primeiro débito é no fim da vigência e o PIX aberto do ciclo é cancelado.</summary>
    [Fact]
    public async Task Trocar_para_o_cartao_comeca_no_fim_da_vigencia_sem_cobrar_em_dobro()
    {
        // Arrange
        var assinatura = AtivaEm(Premium, meio: MeioDePagamento.Pix, recorrencia: null);
        var aberta = CobrancaDaAssinatura.Abrir(assinatura.Id, Premium.Id, MotivoDaCobranca.Ciclo, MeioDePagamento.Pix, 4990);
        _assinaturas.ObterCobrancaAbertaParaEdicao(assinatura.Id, MotivoDaCobranca.Ciclo, Arg.Any<CancellationToken>()).Returns(aberta);

        // Act
        var resultado = await Servico.TrocarMeio(MeioDePagamento.Cartao, "p@turma.com", Ct);

        // Assert
        resultado.Valor.Url.ShouldBe("https://psp/checkout/sessao-1");
        assinatura.Meio.ShouldBe(MeioDePagamento.Pix);
        assinatura.IdExterno.ShouldBe("sessao-1");
        aberta.Situacao.ShouldBe(SituacaoDaCobrancaDoPlano.Cancelada);
        await _provedor
            .Received(1)
            .CriarCheckout(
                Arg.Is<PedidoDeCheckout>(p => p.CobrancaId == null && p.ComecaEm == assinatura.VigenteAte && p.EmailDoPagador == "p@turma.com"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Trocar_para_o_mesmo_meio_e_conflito()
    {
        AtivaEm(Premium);

        var resultado = await Servico.TrocarMeio(MeioDePagamento.Cartao, null, Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("assinatura.mesmo_meio");
    }

    /// <summary>O PIX da renovação abre sete dias antes do vencimento, como o aviso por e-mail.</summary>
    [Theory]
    [InlineData(10, false)]
    [InlineData(25, true)]
    public async Task Pix_da_renovacao_abre_sete_dias_antes(int diasPagos, bool abre)
    {
        AtivaEm(Premium, diasPagos, MeioDePagamento.Pix, recorrencia: null);

        var resultado = await Servico.PagarCiclo("p@turma.com", Ct);

        resultado.Sucesso.ShouldBe(abre);
        if (!abre)
            resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("assinatura.renovacao_ainda_nao_aberta");
    }

    [Fact]
    public async Task Pix_da_renovacao_no_cartao_e_conflito()
    {
        AtivaEm(Premium, diasPagos: 25);

        var resultado = await Servico.PagarCiclo(null, Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("assinatura.renovacao_automatica");
    }
}
