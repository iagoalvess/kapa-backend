using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.MercadoPago.Interfaces;
using Backend.Business.MercadoPago.Models;
using Backend.Business.MercadoPago.Settings;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Pagamentos.Services;
using Backend.Business.Pagamentos.Validators;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Backend.Business.Recebimentos.Services;
using Backend.UnitTests.Loja;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Pagamentos;

/// <summary>
/// As regras do caminho do dinheiro que não dependem do banco: o informe é alegação, a parcela de outro
/// é 404, o PIX sai da chave vigente com o valor do dia, a baixa passa por uma porta só.
/// </summary>
public sealed class PagamentoServiceTests
{
    private static readonly Guid FormaturaId = Guid.CreateVersion7();
    private static readonly MembroDoPerfil Ana = new(Guid.CreateVersion7(), Guid.CreateVersion7(), "Ana", "ana@turma.dev", PapelNaFormatura.Formando);
    private static readonly MembroDoPerfil Bruno = new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "Bruno",
        "bruno@turma.dev",
        PapelNaFormatura.Formando
    );
    private static readonly MembroDoPerfil Tesoureira = new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "Tesa",
        "tesa@turma.dev",
        PapelNaFormatura.Tesoureiro
    );
    private static readonly MembroDoPerfil Comissao = new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "Caio",
        "caio@turma.dev",
        PapelNaFormatura.Comissao
    );

    /// <summary>Multa de 2% e juros de 1% ao mês, sem carência — o que Ana aceitou.</summary>
    private static readonly RegrasDeAtraso RegrasDaAna = new(200, 100, 0, 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IParcelaRepository _parcelas = Substitute.For<IParcelaRepository>();
    private readonly IInformeRepository _informes = Substitute.For<IInformeRepository>();
    private readonly IRecebimentoRepository _recebimentos = Substitute.For<IRecebimentoRepository>();
    private readonly IContaDeRecebimentoRepository _contas = Substitute.For<IContaDeRecebimentoRepository>();
    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();
    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();
    private readonly IArquivoService _arquivos = Substitute.For<IArquivoService>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly IEventoRepository _eventos = Substitute.For<IEventoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IProvedorDaTurmaRepository _provedor = Substitute.For<IProvedorDaTurmaRepository>();
    private readonly IMercadoPago _mercadoPago = Substitute.For<IMercadoPago>();

    private readonly DateOnly _hoje = DataUtils.Hoje();

    public PagamentoServiceTests()
    {
        foreach (var membro in new[] { Ana, Bruno, Tesoureira, Comissao })
            _perfis.ObterMembro(FormaturaId, membro.UsuarioId, Arg.Any<CancellationToken>()).Returns(membro);

        _parcelas
            .ObterRegrasDeAtraso(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, RegrasDeAtraso> { [Ana.VinculoId] = RegrasDaAna });
        _vinculos
            .ListarEmailsDosVinculos(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, string> { [Ana.VinculoId] = Ana.Email });
        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result<ResultadoDaConferencia>>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<Result<ResultadoDaConferencia>>>>()(Ct));
        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<Result>>>()(Ct));
        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result<IReadOnlyList<ParcelaResumo>>>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<Result<IReadOnlyList<ParcelaResumo>>>>>()(Ct));
        _parcelas
            .TravarParaBaixa(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(chamada =>
                (IReadOnlyList<Parcela>)
                    [
                        .. chamada
                            .Arg<IReadOnlyCollection<Guid>>()
                            .Select(_ => Parcela.Nova(Ana.VinculoId, Guid.CreateVersion7(), new ParcelaPrevista(1, _hoje, 350_000))),
                    ]
            );
    }

    private PagamentoService Servico =>
        new(
            _parcelas,
            _informes,
            _contas,
            _perfis,
            _arquivos,
            new EmissaoNoMercadoPago(
                _provedor,
                _mercadoPago,
                Options.Create(new MercadoPagoSettings { ClientId = "app", ClientSecret = "segredo" }),
                _unitOfWork,
                NullLogger<EmissaoNoMercadoPago>.Instance
            ),
            new BaixaAutomatica(
                _provedor,
                _mercadoPago,
                _parcelas,
                _informes,
                _vinculos,
                _formaturas,
                new BaixaService(
                    _recebimentos,
                    _eventos,
                    new EmailsDePagamento(_email, Options.Create(new AplicacaoSettings())),
                    Substitute.For<IQuitacaoDePedidos>(),
                    new ValoresADevolver(Substitute.For<IValorADevolverRepository>())
                ),
                LojaTests.Pagamento(),
                _eventos,
                _recebimentos,
                Substitute.For<IDespesaRepository>(),
                Substitute.For<IOutraReceitaRepository>(),
                new EmailsDePagamento(_email, Options.Create(new AplicacaoSettings())),
                new ValoresADevolver(Substitute.For<IValorADevolverRepository>()),
                new EstornoDaCobranca(Substitute.For<IOutraReceitaRepository>()),
                _unitOfWork,
                NullLogger<BaixaAutomatica>.Instance
            ),
            new NovoInformeValidator(),
            new CartaoTokenizadoValidator(),
            _unitOfWork,
            NullLogger<PagamentoService>.Instance
        );

    private TesourariaService Tesouraria
    {
        get
        {
            var emails = new EmailsDePagamento(_email, Options.Create(new AplicacaoSettings()));

            return new TesourariaService(
                _parcelas,
                _informes,
                _recebimentos,
                _vinculos,
                _formaturas,
                _arquivos,
                new BaixaService(
                    _recebimentos,
                    _eventos,
                    emails,
                    Substitute.For<IQuitacaoDePedidos>(),
                    new ValoresADevolver(Substitute.For<IValorADevolverRepository>())
                ),
                emails,
                new BaixaManualValidator(),
                new ConfirmarInformesValidator(),
                new RecusarInformeValidator(),
                new EstornarBaixaValidator(),
                new CancelarParcelaValidator(),
                _provedor,
                new EstornoDaCobranca(Substitute.For<IOutraReceitaRepository>()),
                new ValoresADevolver(Substitute.For<IValorADevolverRepository>()),
                _eventos,
                _unitOfWork,
                NullLogger<TesourariaService>.Instance
            );
        }
    }

    /// <summary>Uma parcela da Ana, como a leitura a devolve.</summary>
    private ParcelaResumo DaAna(DateOnly vencimento, StatusDaParcela status = StatusDaParcela.Aberta, bool emConferencia = false)
    {
        var parcela = new ParcelaResumo(
            Guid.CreateVersion7(),
            Ana.VinculoId,
            Ana.UsuarioId,
            Ana.Nome,
            Guid.CreateVersion7(),
            TipoDeCobranca.Mensalidade,
            null,
            3,
            24,
            vencimento,
            350_000,
            Parcela.StatusNoDia(status, vencimento, _hoje),
            emConferencia
        );
        _parcelas.Obter(parcela.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(parcela);

        return parcela;
    }

    private static ContaDeRecebimentoDetalhe Conta(bool conferida) => Conta(conferida, SoPix);

    private static ContaDeRecebimentoDetalhe Conta(bool conferida, MeiosDaConta meios) =>
        new(meios, DateTime.UtcNow, conferida ? DateTime.UtcNow : null, null);

    private static readonly ChavePixDaConta ChaveDaComissao = new(TipoDeChavePix.Cpf, "52998224725", "Comissão Medicina", "Curitiba");

    private static readonly MeiosDaConta SoPix = new(ChaveDaComissao, null, null);

    [Fact]
    public async Task Informe_grava_pendente_e_nao_mexe_na_parcela()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));

        // Act
        var resultado = await Servico.Informar(
            FormaturaId,
            Ana.UsuarioId,
            [parcela.Id],
            new NovoInforme(_hoje, 350_000, MeioDeRecebimento.Pix),
            null,
            Ct
        );

        // Assert
        resultado.Valor[0].Status.ShouldBe(StatusDaParcela.Aberta);
        resultado.Valor[0].EmConferencia.ShouldBeTrue();
        await _informes
            .Received(1)
            .Adicionar(
                Arg.Is<InformeDePagamento>(i => i.ParcelaId == parcela.Id && i.Status == StatusDoInforme.Pendente && i.VinculoId == Ana.VinculoId),
                Ct
            );
        await _parcelas.Received(1).TravarParaBaixa(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(parcela.Id)), Ct);
        await _unitOfWork.Received(1).EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result<IReadOnlyList<ParcelaResumo>>>>>(), Ct);
    }

    [Fact]
    public async Task Parcela_de_outro_formando_e_404_no_informe_no_pix_e_na_leitura()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));

        // Act
        var informe = await Servico.Informar(
            FormaturaId,
            Bruno.UsuarioId,
            [parcela.Id],
            new NovoInforme(_hoje, 350_000, MeioDeRecebimento.Pix),
            null,
            Ct
        );
        var pix = await Servico.GerarCobranca(FormaturaId, Bruno.UsuarioId, parcela.Id, Ct);
        var leitura = await Servico.ObterParcela(FormaturaId, Bruno.UsuarioId, parcela.Id, Ct);

        // Assert
        informe.PrimeiroErro.Tipo.ShouldBe(ETipoErro.NaoEncontrado);
        pix.PrimeiroErro.Tipo.ShouldBe(ETipoErro.NaoEncontrado);
        leitura.PrimeiroErro.Tipo.ShouldBe(ETipoErro.NaoEncontrado);
        await _informes.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>A gestão lê a parcela de qualquer um; o PIX é só do dono e da tesouraria.</summary>
    [Fact]
    public async Task Comissao_le_a_parcela_mas_nao_ve_o_pix_e_a_tesouraria_ve_os_dois()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));

        // Act
        var leituraDaComissao = await Servico.ObterParcela(FormaturaId, Comissao.UsuarioId, parcela.Id, Ct);
        var pixDaComissao = await Servico.GerarCobranca(FormaturaId, Comissao.UsuarioId, parcela.Id, Ct);
        var pixDaTesouraria = await Servico.GerarCobranca(FormaturaId, Tesoureira.UsuarioId, parcela.Id, Ct);

        // Assert
        leituraDaComissao.Sucesso.ShouldBeTrue();
        pixDaComissao.PrimeiroErro.Tipo.ShouldBe(ETipoErro.NaoEncontrado);
        pixDaTesouraria.Sucesso.ShouldBeTrue();
    }

    [Fact]
    public async Task Segundo_informe_e_informe_em_parcela_paga_devolvem_409()
    {
        // Arrange
        var emConferencia = DaAna(_hoje.AddDays(5), emConferencia: true);
        var paga = DaAna(_hoje.AddDays(-5), StatusDaParcela.Paga);

        // Act
        var segundo = await Servico.Informar(
            FormaturaId,
            Ana.UsuarioId,
            [emConferencia.Id],
            new NovoInforme(_hoje, 350_000, MeioDeRecebimento.Pix),
            null,
            Ct
        );
        var naPaga = await Servico.Informar(FormaturaId, Ana.UsuarioId, [paga.Id], new NovoInforme(_hoje, 350_000, MeioDeRecebimento.Pix), null, Ct);

        // Assert
        segundo.PrimeiroErro.Codigo.ShouldBe("pagamento.informe_pendente");
        naPaga.PrimeiroErro.Codigo.ShouldBe("pagamento.parcela_paga");
        naPaga.PrimeiroErro.Mensagem.ShouldContain("fale com a tesouraria");
    }

    /// <summary>
    /// Um PIX cobrindo três meses atrasados: o valor é distribuído da parcela mais antiga para a mais
    /// nova, cada uma até o que ela cobra, e o que sobra fica na última.
    /// </summary>
    [Fact]
    public async Task Um_pagamento_cobre_varias_parcelas_da_mais_antiga_para_a_mais_nova()
    {
        // Arrange
        var maisNova = DaAna(_hoje.AddDays(5));
        var maisAntiga = DaAna(_hoje.AddDays(-35));
        var doMeio = DaAna(_hoje.AddDays(-5));

        // Act
        var resultado = await Servico.Informar(
            FormaturaId,
            Ana.UsuarioId,
            [maisNova.Id, maisAntiga.Id, doMeio.Id],
            new NovoInforme(_hoje, 800_000, MeioDeRecebimento.Pix),
            null,
            Ct
        );

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        resultado.Valor.Select(p => p.Id).ShouldBe([maisAntiga.Id, doMeio.Id, maisNova.Id]);
        resultado.Valor.ShouldAllBe(p => p.EmConferencia);
        await _informes.Received(3).Adicionar(Arg.Any<InformeDePagamento>(), Ct);
        // Cada vencida consome o valor do dia dela, com a multa e os juros que Ana aceitou; a última leva o resto.
        await _informes.Received(1).Adicionar(Arg.Is<InformeDePagamento>(i => i.ParcelaId == maisAntiga.Id && i.ValorEmCentavos == 361_083), Ct);
        await _informes.Received(1).Adicionar(Arg.Is<InformeDePagamento>(i => i.ParcelaId == doMeio.Id && i.ValorEmCentavos == 357_583), Ct);
        await _informes.Received(1).Adicionar(Arg.Is<InformeDePagamento>(i => i.ParcelaId == maisNova.Id && i.ValorEmCentavos == 81_334), Ct);
        await _unitOfWork.Received(1).EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result<IReadOnlyList<ParcelaResumo>>>>>(), Ct);
    }

    /// <summary>Uma parcela recusada derruba o aviso inteiro: nada é gravado pela metade.</summary>
    [Fact]
    public async Task Parcela_ja_paga_no_meio_do_lote_recusa_o_aviso_inteiro()
    {
        // Arrange
        var aberta = DaAna(_hoje.AddDays(-5));
        var paga = DaAna(_hoje.AddDays(-35), StatusDaParcela.Paga);

        // Act
        var resultado = await Servico.Informar(
            FormaturaId,
            Ana.UsuarioId,
            [aberta.Id, paga.Id],
            new NovoInforme(_hoje, 700_000, MeioDeRecebimento.Pix),
            null,
            Ct
        );

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("pagamento.parcela_paga");
        await _informes.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Aviso_sem_parcela_nenhuma_e_recusado()
    {
        // Act
        var resultado = await Servico.Informar(FormaturaId, Ana.UsuarioId, [], new NovoInforme(_hoje, 350_000, MeioDeRecebimento.Pix), null, Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("pagamento.parcelas_do_informe");
        resultado.PrimeiroErro.Campo.ShouldBe("parcela_ids");
    }

    [Fact]
    public async Task Informe_com_data_no_futuro_e_recusado_na_forma()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));

        // Act
        var resultado = await Servico.Informar(
            FormaturaId,
            Ana.UsuarioId,
            [parcela.Id],
            new NovoInforme(_hoje.AddDays(1), 350_000, MeioDeRecebimento.Pix),
            null,
            Ct
        );

        // Assert
        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Validacao);
        resultado.PrimeiroErro.Campo.ShouldBe("pago_em");
    }

    /// <summary>P3 de 14/09/2026: só turma sem conta responde 409; conta não conferida mostra o PIX.</summary>
    [Fact]
    public async Task Pix_sai_da_chave_vigente_mesmo_nao_conferida_e_sem_conta_e_409()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: false), (ContaDeRecebimentoDetalhe?)null);

        // Act
        var comConta = await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);
        var semConta = await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        var pix = comConta.Valor.Meios.ShouldHaveSingleItem();
        pix.Meio.ShouldBe(MeioDeRecebimento.Pix);
        pix.Pix!.CopiaECola.ShouldContain("52998224725");
        pix.Pix.CopiaECola.ShouldContain(comConta.Valor.Identificador);
        comConta.Valor.Identificador.ShouldBe(PagamentoService.Identificador(parcela.Id));
        comConta.Valor.Identificador.Length.ShouldBe(25);
        semConta.PrimeiroErro.Codigo.ShouldBe("pagamento.sem_conta");
        semConta.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Conflito);
    }

    /// <summary>O passo 1 do lote: um BR Code só, com a soma, e o identificador da parcela mais antiga.</summary>
    [Fact]
    public async Task Pix_de_varias_soma_o_valor_do_dia_e_usa_o_identificador_da_mais_antiga()
    {
        // Arrange
        var vencida = DaAna(_hoje.AddDays(-30));
        var aVencer = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));

        // Act
        var pix = await Servico.GerarCobrancaDeVarias(FormaturaId, Ana.UsuarioId, [aVencer.Id, vencida.Id], Ct);

        // Assert
        pix.Valor.ValorEmCentavos.ShouldBe(350_000 + 7_000 + 3_500 + 350_000);
        pix.Valor.Identificador.ShouldBe(PagamentoService.Identificador(vencida.Id));
        pix.Valor.Meios.ShouldHaveSingleItem().Pix!.CopiaECola.ShouldContain("54077105.00");
    }

    /// <summary>Quem não pode avisar também não vê o QR: uma parcela ruim derruba o lote inteiro.</summary>
    [Fact]
    public async Task Pix_de_varias_recusa_lista_vazia_parcela_de_outro_e_parcela_ja_avisada()
    {
        // Arrange
        var daAna = DaAna(_hoje.AddDays(5));
        var avisada = DaAna(_hoje.AddDays(35), emConferencia: true);
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));

        // Act
        var vazio = await Servico.GerarCobrancaDeVarias(FormaturaId, Ana.UsuarioId, [], Ct);
        var deOutro = await Servico.GerarCobrancaDeVarias(FormaturaId, Bruno.UsuarioId, [daAna.Id], Ct);
        var comAvisada = await Servico.GerarCobrancaDeVarias(FormaturaId, Ana.UsuarioId, [daAna.Id, avisada.Id], Ct);

        // Assert
        vazio.PrimeiroErro.Codigo.ShouldBe("pagamento.parcelas_do_informe");
        vazio.PrimeiroErro.Campo.ShouldBe("parcela_ids");
        deOutro.PrimeiroErro.Tipo.ShouldBe(ETipoErro.NaoEncontrado);
        comAvisada.PrimeiroErro.Codigo.ShouldBe("pagamento.informe_pendente");
    }

    [Fact]
    public async Task Pix_da_vencida_cobra_o_valor_do_dia_pelas_regras_aceitas()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(-30));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));

        // Act
        var pix = await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        pix.Valor.ValorEmCentavos.ShouldBe(350_000 + 7_000 + 3_500);
        pix.Valor.Meios.ShouldHaveSingleItem().Pix!.CopiaECola.ShouldContain("54073605.00");
    }

    /// <summary>
    /// Decisão 2 da Sprint 18: a cobrança traz um item por meio habilitado, cada um com o que a tela
    /// mostra. Com um meio só não há seletor — é a mesma tela de antes da sprint.
    /// </summary>
    [Fact]
    public async Task Cobranca_traz_um_item_por_meio_habilitado_com_o_que_cada_um_mostra()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        var conta = new DadosBancarios("Banco do Brasil", "1234-5", "98765-4", "Corrente", "Comissão Medicina");
        var meios = new MeiosDaConta(ChaveDaComissao, conta, new DinheiroComAlguem("Ana Souza", "na sala 12"));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true, meios));

        // Act
        var cobranca = await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        cobranca.Valor.Meios.Select(m => m.Meio).ShouldBe([MeioDeRecebimento.Pix, MeioDeRecebimento.Transferencia, MeioDeRecebimento.Dinheiro]);
        cobranca.Valor.Meios[0].Pix!.CopiaECola.ShouldContain("52998224725");
        cobranca.Valor.Meios[1].Transferencia.ShouldBe(conta);
        cobranca.Valor.Meios[2].Instrucao.ShouldBe("Entregue a Ana Souza, na sala 12.");
    }

    /// <summary>P4 de 21/09/2026: sem PIX a cobrança continua existindo, e nenhum item traz BR Code.</summary>
    [Fact]
    public async Task Turma_sem_pix_cobra_pelo_meio_que_ela_habilitou()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        var meios = new MeiosDaConta(null, null, new DinheiroComAlguem("Ana Souza", null));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: false, meios));

        // Act
        var cobranca = await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        var dinheiro = cobranca.Valor.Meios.ShouldHaveSingleItem();
        dinheiro.Meio.ShouldBe(MeioDeRecebimento.Dinheiro);
        dinheiro.Pix.ShouldBeNull();
        dinheiro.Instrucao.ShouldBe("Entregue a Ana Souza.");
    }

    /// <summary>Decisão 4: o meio do aviso vira a forma da baixa, em vez de a tesouraria adivinhar PIX.</summary>
    [Theory]
    [InlineData(MeioDeRecebimento.Dinheiro, FormaDePagamento.Dinheiro)]
    [InlineData(MeioDeRecebimento.Transferencia, FormaDePagamento.Transferencia)]
    [InlineData(MeioDeRecebimento.Outro, FormaDePagamento.Outro)]
    [InlineData(MeioDeRecebimento.Pix, FormaDePagamento.Pix)]
    public async Task Conferencia_baixa_com_a_forma_do_meio_informado(MeioDeRecebimento meio, FormaDePagamento forma)
    {
        // Arrange
        var parcela = Parcela.Nova(Ana.VinculoId, Guid.CreateVersion7(), new ParcelaPrevista(3, _hoje, 350_000));
        var informe = InformeDePagamento.Novo(parcela.Id, Ana.VinculoId, _hoje, 350_000, null, meio);
        Preparar(parcela, informe);

        // Act
        await Tesouraria.Confirmar(
            FormaturaId,
            Tesoureira.UsuarioId,
            null,
            new ConfirmarInformes([new ConfirmacaoDeInforme(informe.Id, 350_000)]),
            Ct
        );

        // Assert
        await _recebimentos.Received(1).Adicionar(Arg.Is<Recebimento>(r => r.Forma == forma), Arg.Any<CancellationToken>());
    }

    /// <summary>Aviso anterior à Sprint 18 não tem meio, e continua baixando como PIX — era o único caminho.</summary>
    [Fact]
    public async Task Aviso_antigo_sem_meio_baixa_como_pix()
    {
        // Arrange
        var parcela = Parcela.Nova(Ana.VinculoId, Guid.CreateVersion7(), new ParcelaPrevista(3, _hoje, 350_000));
        var informe = InformeDePagamento.Novo(parcela.Id, Ana.VinculoId, _hoje, 350_000, null, null);
        Preparar(parcela, informe);

        // Act
        await Tesouraria.Confirmar(
            FormaturaId,
            Tesoureira.UsuarioId,
            null,
            new ConfirmarInformes([new ConfirmacaoDeInforme(informe.Id, 350_000)]),
            Ct
        );

        // Assert
        await _recebimentos.Received(1).Adicionar(Arg.Is<Recebimento>(r => r.Forma == FormaDePagamento.Pix), Arg.Any<CancellationToken>());
    }

    /// <summary>P6 e P7 de 21/09/2026: o dinheiro entra sem comprovante, e o meio fora da lista não é barrado.</summary>
    [Fact]
    public async Task Informe_em_dinheiro_sem_comprovante_e_aceito_e_grava_o_meio()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));

        // Act
        var resultado = await Servico.Informar(
            FormaturaId,
            Ana.UsuarioId,
            [parcela.Id],
            new NovoInforme(_hoje, 350_000, MeioDeRecebimento.Dinheiro),
            null,
            Ct
        );

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _informes
            .Received(1)
            .Adicionar(
                Arg.Is<InformeDePagamento>(i => i.MeioEscolhido == MeioDeRecebimento.Dinheiro && i.ComprovanteArquivoId == null),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Recusa_exige_motivo()
    {
        // Act
        var resultado = await Tesouraria.Recusar(FormaturaId, Tesoureira.UsuarioId, Guid.CreateVersion7(), new RecusarInforme("  "), Ct);

        // Assert
        resultado.PrimeiroErro.Campo.ShouldBe("motivo");
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Recusa_grava_o_motivo_e_avisa_o_formando_sem_mexer_na_parcela()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        var informe = InformeDePagamento.Novo(parcela.Id, Ana.VinculoId, _hoje, 350_000, null, MeioDeRecebimento.Pix);
        _informes.ListarParcelas(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([parcela.Id]);
        _informes.ListarParaEdicao(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([informe]);

        // Act
        var resultado = await Tesouraria.Recusar(
            FormaturaId,
            Tesoureira.UsuarioId,
            informe.Id,
            new RecusarInforme("Não achei no extrato de abril."),
            Ct
        );

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        informe.Status.ShouldBe(StatusDoInforme.Recusado);
        informe.ConferidoPorUsuarioId.ShouldBe(Tesoureira.UsuarioId);
        await _email.Received(1).Enfileirar(Arg.Is<NovoEmail>(e => e.Para == Ana.Email && e.CorpoHtml.Contains("extrato de abril")), Ct);
        await _parcelas.Received(1).TravarParaBaixa(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(parcela.Id)), Ct);
    }

    /// <summary>
    /// Recusar trava a parcela, como confirmar: o segundo a chegar encontra o informe já conferido, em vez
    /// de os dois darem certo — dois e-mails contraditórios e, às vezes, recusa sobre parcela paga.
    /// </summary>
    [Fact]
    public async Task Recusa_de_informe_ja_confirmado_e_conflito()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        var informe = InformeDePagamento.Novo(parcela.Id, Ana.VinculoId, _hoje, 350_000, null, MeioDeRecebimento.Pix);
        informe.Confirmar(Tesoureira.UsuarioId, DateTime.UtcNow);
        _informes.ListarParcelas(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([parcela.Id]);
        _informes.ListarParaEdicao(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([informe]);

        // Act
        var resultado = await Tesouraria.Recusar(FormaturaId, Tesoureira.UsuarioId, informe.Id, new RecusarInforme("tarde demais"), Ct);

        // Assert
        resultado.Falhou.ShouldBeTrue();
        await _email.DidNotReceiveWithAnyArgs().Enfileirar(default!, Ct);
    }

    /// <summary>Aviso e baixa manual juntos: sob a trava, a baixa encontra o aviso e para.</summary>
    [Fact]
    public async Task Informe_chegando_com_aviso_ja_gravado_sob_a_trava_e_conflito()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _informes.ExistePendente(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var resultado = await Servico.Informar(
            FormaturaId,
            Ana.UsuarioId,
            [parcela.Id],
            new NovoInforme(_hoje, 350_000, MeioDeRecebimento.Dinheiro),
            null,
            Ct
        );

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("pagamento.informe_pendente");
        await _informes.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>A baixa grava tudo no mesmo lote: parcela, recebimento com o devido, informe, e-mail e auditoria.</summary>
    [Fact]
    public async Task Confirmar_baixa_a_parcela_com_o_recebido_e_guarda_o_devido_do_dia()
    {
        // Arrange
        var parcela = Parcela.Nova(Ana.VinculoId, Guid.CreateVersion7(), new ParcelaPrevista(3, _hoje.AddDays(-30), 350_000));
        var informe = InformeDePagamento.Novo(parcela.Id, Ana.VinculoId, _hoje, 350_000, null, MeioDeRecebimento.Pix);
        Preparar(parcela, informe);

        // Act
        var resultado = await Tesouraria.Confirmar(
            FormaturaId,
            Tesoureira.UsuarioId,
            "203.0.113.7",
            new ConfirmarInformes([new ConfirmacaoDeInforme(informe.Id, 350_000)]),
            Ct
        );

        // Assert
        resultado.Valor.ShouldBe(new ResultadoDaConferencia(1, 0));
        parcela.Status.ShouldBe(StatusDaParcela.Paga);
        parcela.ValorPagoEmCentavos.ShouldBe(350_000);
        parcela.PagoEm.ShouldBe(_hoje);
        informe.Status.ShouldBe(StatusDoInforme.Confirmado);
        await _recebimentos
            .Received(1)
            .Adicionar(
                Arg.Is<Recebimento>(r =>
                    r.ParcelaId == parcela.Id
                    && r.InformeId == informe.Id
                    && r.ValorEmCentavos == 350_000
                    && r.DevidoEmCentavos == 360_500
                    && r.BaixadoPorUsuarioId == Tesoureira.UsuarioId
                    && r.EnderecoIp == "203.0.113.7"
                ),
                Arg.Any<CancellationToken>()
            );
        await _eventos.Received(1).Adicionar(Arg.Is<Evento>(e => e.Nome == BaixaService.EventoDeBaixa), Arg.Any<CancellationToken>());
        await _email
            .Received(1)
            .Enfileirar(Arg.Is<NovoEmail>(e => e.Para == Ana.Email && e.Assunto.StartsWith("Pagamento confirmado")), Arg.Any<CancellationToken>());
    }

    /// <summary>O clique duplo: a segunda confirmação encontra o informe conferido e não baixa de novo.</summary>
    [Fact]
    public async Task Informe_ja_conferido_e_ignorado_sem_segunda_baixa()
    {
        // Arrange
        var parcela = Parcela.Nova(Ana.VinculoId, Guid.CreateVersion7(), new ParcelaPrevista(3, _hoje, 350_000));
        var informe = InformeDePagamento.Novo(parcela.Id, Ana.VinculoId, _hoje, 350_000, null, MeioDeRecebimento.Pix);
        informe.Confirmar(Tesoureira.UsuarioId, DateTime.UtcNow);
        Preparar(parcela, informe);

        // Act
        var resultado = await Tesouraria.Confirmar(
            FormaturaId,
            Tesoureira.UsuarioId,
            null,
            new ConfirmarInformes([new ConfirmacaoDeInforme(informe.Id, 350_000)]),
            Ct
        );

        // Assert
        resultado.Valor.ShouldBe(new ResultadoDaConferencia(0, 1));
        parcela.Status.ShouldBe(StatusDaParcela.Aberta);
        await _recebimentos.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    [Fact]
    public async Task Informe_de_outra_turma_derruba_o_lote_com_404()
    {
        // Arrange
        _informes.ListarParcelas(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        _parcelas.TravarParaBaixa(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        _informes.ListarParaEdicao(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var resultado = await Tesouraria.Confirmar(
            FormaturaId,
            Tesoureira.UsuarioId,
            null,
            new ConfirmarInformes([new ConfirmacaoDeInforme(Guid.CreateVersion7(), 350_000)]),
            Ct
        );

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("pagamento.informe_nao_encontrado");
    }

    /// <summary>A parcela volta a ser devida: quem vai pagar de novo precisa saber disso, e por quê.</summary>
    [Fact]
    public async Task Estorno_avisa_o_formando_com_a_justificativa()
    {
        // Arrange
        var parcela = Parcela.Nova(Ana.VinculoId, Guid.CreateVersion7(), new ParcelaPrevista(3, _hoje.AddDays(-5), 350_000));
        parcela.Pagar(350_000, _hoje, 350_000).Valor.ShouldBeTrue();
        var recebimento = Recebimento.Novo(
            parcela.Id,
            null,
            new DadosDaBaixa(FormaDePagamento.Dinheiro, _hoje, 350_000, null, Tesoureira.UsuarioId, null, DateTime.UtcNow),
            350_000
        );
        _parcelas.TravarParaBaixa(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([parcela]);
        _recebimentos.ObterAtivoParaEdicao(parcela.Id, Arg.Any<CancellationToken>()).Returns(recebimento);
        var relida = DaAna(parcela.Vencimento) with { Id = parcela.Id };
        _parcelas.Obter(parcela.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(relida);

        // Act
        var resultado = await Tesouraria.Estornar(FormaturaId, Tesoureira.UsuarioId, null, parcela.Id, new EstornarBaixa("Dinheiro devolvido."), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        parcela.Status.ShouldBe(StatusDaParcela.Aberta);
        recebimento.EstornadoEm.ShouldNotBeNull();
        await _email
            .Received(1)
            .Enfileirar(
                Arg.Is<NovoEmail>(e =>
                    e.Para == Ana.Email && e.Assunto.StartsWith("Pagamento estornado") && e.CorpoHtml.Contains("Dinheiro devolvido.")
                ),
                Arg.Any<CancellationToken>()
            );
        await _eventos.Received(1).Adicionar(Arg.Is<Evento>(e => e.Nome == BaixaService.EventoDeEstorno), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Baixa_manual_de_parcela_com_aviso_pendente_manda_para_a_conferencia()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5), emConferencia: true);

        // Act
        var resultado = await Tesouraria.BaixarManualmente(
            FormaturaId,
            Tesoureira.UsuarioId,
            null,
            parcela.Id,
            new BaixaManual(FormaDePagamento.Dinheiro, _hoje, 350_000),
            null,
            Ct
        );

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("pagamento.informe_pendente");
    }

    private void Preparar(Parcela parcela, InformeDePagamento informe)
    {
        _informes.ListarParcelas(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([parcela.Id]);
        _parcelas.TravarParaBaixa(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([parcela]);
        _informes.ListarParaEdicao(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([informe]);
    }

    [Fact]
    public async Task Cobranca_leva_documento_do_titular_e_a_conferencia()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        var conferidaEm = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(new ContaDeRecebimentoDetalhe(SoPix, DateTime.UtcNow, conferidaEm, "Ana"));

        // Act
        var cobranca = await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        var pix = cobranca.Valor.Meios.ShouldHaveSingleItem().Pix!;
        pix.DocumentoDoTitular.ShouldBe("CPF ***.982.247-**");
        pix.ConferidaEm.ShouldBe(conferidaEm);
    }

    /// <summary>
    /// Na cobrança automática (29/09/2026) o dono da parcela vê só o PIX do Mercado Pago — os meios da comissão
    /// saem da tela. A emissão reserva antes de chamar (decisão 12a da Sprint 25).
    /// </summary>
    [Fact]
    public async Task Na_cobranca_automatica_o_dono_ve_so_o_mercado_pago()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));
        PrepararMercadoPago();
        _mercadoPago
            .Emitir("token", Arg.Any<PedidoDeCobranca>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new DocumentoEmitido("ORD1", "00020126-dinamico")));

        // Act
        var cobranca = await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        cobranca.Valor.PeloMercadoPago.ShouldHaveSingleItem().Meio.ShouldBe(MeioDePagamento.Pix);
        cobranca.Valor.PeloMercadoPago[0].Pix!.CopiaECola.ShouldBe("00020126-dinamico");
        cobranca.Valor.Meios.ShouldBeEmpty();
        _reservada!.Status.ShouldBe(StatusDaCobrancaBancaria.Emitida);
        _reservada.ParcelaIds.ShouldBe([parcela.Id]);
        await _mercadoPago
            .Received(1)
            .Emitir(
                "token",
                Arg.Is<PedidoDeCobranca>(p => p.Referencia == _reservada.Id && p.Meio == MeioDePagamento.Pix && p.Pagador.Email == Ana.Email),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>
    /// Decisão 12a: sem resposta do Mercado Pago, a tela pede para tentar de novo — a chave da comissão não entra no
    /// lugar (29/09/2026) — e a reserva continua em <c>Emitindo</c>, porque o pedido pode ter nascido lá e a próxima
    /// tentativa tem de usar a mesma chave.
    /// </summary>
    [Fact]
    public async Task Mercado_pago_sem_resposta_pede_para_tentar_de_novo_e_guarda_a_reserva()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));
        PrepararMercadoPago();
        _mercadoPago
            .Emitir(Arg.Any<string>(), Arg.Any<PedidoDeCobranca>(), Arg.Any<CancellationToken>())
            .Returns(Result.Falha<DocumentoEmitido>(Erro.Indisponivel("recebimento.provedor_indisponivel", "fora")));

        // Act
        var cobranca = await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        cobranca.PrimeiroErro.Codigo.ShouldBe("pagamento.mercado_pago_indisponivel");
        _reservada!.Status.ShouldBe(StatusDaCobrancaBancaria.Emitindo);
    }

    /// <summary>Conectada mas na cobrança manual: só os meios da comissão, e nada é emitido no Mercado Pago.</summary>
    [Fact]
    public async Task Na_cobranca_manual_o_mercado_pago_conectado_fica_fora_da_tela()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));
        PrepararMercadoPago(cartaoLigado: true, automatica: false);

        // Act
        var cobranca = await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        cobranca.Valor.PeloMercadoPago.ShouldBeEmpty();
        cobranca.Valor.Meios.ShouldHaveSingleItem().Meio.ShouldBe(MeioDeRecebimento.Pix);
        await _mercadoPago.DidNotReceiveWithAnyArgs().Emitir(default!, default!, Ct);
    }

    /// <summary>Na cobrança automática não há "já paguei": quem pagou por fora fala com a tesouraria, que dá baixa.</summary>
    [Fact]
    public async Task Na_cobranca_automatica_o_formando_nao_avisa_pagamento()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        PrepararMercadoPago();

        // Act
        var resultado = await Servico.Informar(
            FormaturaId,
            Ana.UsuarioId,
            [parcela.Id],
            new NovoInforme(_hoje, 350_000, MeioDeRecebimento.Pix),
            null,
            Ct
        );

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("pagamento.aviso_desligado");
        await _informes.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>Na cobrança manual o cartão não passa, mesmo ligado: ele é do Mercado Pago.</summary>
    [Fact]
    public async Task Na_cobranca_manual_o_cartao_e_recusado()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        PrepararMercadoPago(cartaoLigado: true, automatica: false);

        // Act
        var pago = await Servico.PagarNoCartao(
            FormaturaId,
            Ana.UsuarioId,
            new PagamentoNoCartao([parcela.Id], new CartaoTokenizado("tok-1", "visa", 1), ValorDaParcela),
            Ct
        );

        // Assert
        pago.PrimeiroErro.Codigo.ShouldBe("pagamento.cartao_desligado");
        await _mercadoPago.DidNotReceiveWithAnyArgs().Emitir(default!, default!, Ct);
    }

    /// <summary>Decisão 12a: recusa do Mercado Pago (4xx) é definitiva — a reserva é solta.</summary>
    [Fact]
    public async Task Mercado_pago_que_recusa_solta_a_reserva()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));
        PrepararMercadoPago();
        _mercadoPago
            .Emitir(Arg.Any<string>(), Arg.Any<PedidoDeCobranca>(), Arg.Any<CancellationToken>())
            .Returns(Result.Falha<DocumentoEmitido>(Erro.Conflito("recebimento.provedor_recusou", "não")));

        // Act
        await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        _reservada!.Status.ShouldBe(StatusDaCobrancaBancaria.Falhou);
    }

    /// <summary>
    /// Decisão 12a: a reserva que ficou sem resposta é retomada com o mesmo id — a mesma chave de
    /// idempotência —, e não nasce outra cobrança.
    /// </summary>
    [Fact]
    public async Task Reserva_sem_resposta_e_retomada_com_a_mesma_chave()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));
        PrepararMercadoPago();
        var pendente = new CobrancaBancaria(
            MeioDePagamento.Pix,
            1,
            [parcela.Id],
            350_000,
            DateTime.UtcNow.AddHours(2),
            CobrancaBancaria.ChaveDoPix(1, [parcela.Id], 350_000, _hoje)
        );
        typeof(Entity).GetProperty(nameof(Entity.CriadoEm))!.SetValue(pendente, DateTime.UtcNow.AddMinutes(-2));
        _reservada = pendente;
        _provedor.ObterViva(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(pendente);
        _mercadoPago
            .Emitir("token", Arg.Any<PedidoDeCobranca>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new DocumentoEmitido("ORD1", "00020126-retomado")));

        // Act
        var cobranca = await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        cobranca.Valor.PeloMercadoPago[0].Pix!.CopiaECola.ShouldBe("00020126-retomado");
        pendente.Status.ShouldBe(StatusDaCobrancaBancaria.Emitida);
        await _provedor.DidNotReceiveWithAnyArgs().ReservarEmissao(default!, Ct);
        await _mercadoPago.Received(1).Emitir("token", Arg.Is<PedidoDeCobranca>(p => p.Referencia == pendente.Id), Arg.Any<CancellationToken>());
    }

    /// <summary>Decisão 4: a segunda aba do mesmo formando reaproveita o PIX vivo, sem emitir outro.</summary>
    [Fact]
    public async Task Pix_dinamico_vivo_e_reaproveitado_sem_nova_emissao()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));
        PrepararMercadoPago();
        var viva = new CobrancaBancaria(
            MeioDePagamento.Pix,
            1,
            [parcela.Id],
            350_000,
            DateTime.UtcNow.AddHours(2),
            CobrancaBancaria.ChaveDoPix(1, [parcela.Id], 350_000, _hoje)
        );
        viva.Emitida("ORD1", "00020126-vivo");
        _provedor.ObterViva(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(viva);

        // Act
        var cobranca = await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        cobranca.Valor.PeloMercadoPago[0].Pix!.CopiaECola.ShouldBe("00020126-vivo");
        await _mercadoPago.DidNotReceiveWithAnyArgs().Emitir(default!, default!, Ct);
        await _provedor.DidNotReceiveWithAnyArgs().ReservarEmissao(default!, Ct);
    }

    /// <summary>A tesouraria vê a cobrança de qualquer formando, mas não paga: nada é emitido em nome dela.</summary>
    [Fact]
    public async Task Tesouraria_vendo_a_parcela_nao_emite_pix_dinamico()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));
        PrepararMercadoPago();
        _perfis.ObterTitular(FormaturaId, Tesoureira.UsuarioId, Arg.Any<CancellationToken>()).Returns(Tesoureira);

        // Act
        var cobranca = await Servico.GerarCobranca(FormaturaId, Tesoureira.UsuarioId, parcela.Id, Ct);

        // Assert
        cobranca.Valor.PeloMercadoPago.ShouldBeEmpty();
        cobranca.Valor.Meios.ShouldBeEmpty();
        await _mercadoPago.DidNotReceiveWithAnyArgs().Emitir(default!, default!, Ct);
    }

    /// <summary>
    /// Sprint 39, P2: com o cartão ligado e a taxa repassada, o cartão aparece depois do PIX, com o valor que faz a
    /// turma receber o do PIX inteiro — o bruto é o líquido dividido por (1 − taxa).
    /// </summary>
    [Fact]
    public async Task Com_o_cartao_ligado_o_dono_ve_o_cartao_com_a_taxa_repassada()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));
        PrepararMercadoPago(cartaoLigado: true, taxaRepassada: 500);
        _mercadoPago
            .Emitir("token", Arg.Any<PedidoDeCobranca>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new DocumentoEmitido("ORD1", "00020126-dinamico")));

        // Act
        var cobranca = await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        var valor = cobranca.Valor.ValorEmCentavos;
        cobranca.Valor.PeloMercadoPago.Select(m => m.Meio).ShouldBe([MeioDePagamento.Pix, MeioDePagamento.Cartao]);
        var noCartao = cobranca.Valor.PeloMercadoPago[1].Cartao!;
        noCartao.ChavePublica.ShouldBe("APP_USR-publica");
        noCartao.ValorEmCentavos.ShouldBe((long)Math.Ceiling(valor / 0.95m));
        noCartao.AcrescimoEmCentavos.ShouldBe(noCartao.ValorEmCentavos - valor);
        noCartao.MaximoDeParcelas.ShouldBe(12);
    }

    /// <summary>Sprint 39, P7: com o cartão desligado, a opção não aparece.</summary>
    [Fact]
    public async Task Com_o_cartao_desligado_so_o_pix_aparece()
    {
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));
        PrepararMercadoPago();
        _mercadoPago
            .Emitir("token", Arg.Any<PedidoDeCobranca>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new DocumentoEmitido("ORD1", "00020126-dinamico")));

        var cobranca = await Servico.GerarCobranca(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        cobranca.Valor.PeloMercadoPago.ShouldHaveSingleItem().Meio.ShouldBe(MeioDePagamento.Pix);
    }

    /// <summary>
    /// Sprint 39: o cartão cobra o valor com o acréscimo, com o token do formulário, e a baixa da consulta paga as
    /// parcelas na hora.
    /// </summary>
    [Fact]
    public async Task Cartao_aprovado_cobra_o_valor_com_o_acrescimo_e_baixa_na_hora()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        PrepararMercadoPago(cartaoLigado: true, taxaRepassada: 500);
        var noCartao = (long)Math.Ceiling(ValorDaParcela / 0.95m);
        _mercadoPago
            .Emitir("token", Arg.Any<PedidoDeCobranca>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new DocumentoEmitido("ORD9", null, SituacaoDoPedido.Pago)));
        _provedor.ObterCobranca(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => _reservada);
        _provedor.TravarCobranca(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => _reservada);
        _mercadoPago
            .ConsultarPedido("token", "ORD9", Arg.Any<CancellationToken>())
            .Returns(_ => Result.Ok(new PedidoConsultado("ORD9", _reservada!.Id.ToString("N"), SituacaoDoPedido.Pago, noCartao, DateTime.UtcNow)));
        _mercadoPago
            .BuscarPagamentoAprovado(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok<PagamentoNoMercadoPago?>(null));
        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result<bool>>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<Result<bool>>>>()(Ct));

        // Act
        var pago = await Servico.PagarNoCartao(
            FormaturaId,
            Ana.UsuarioId,
            new PagamentoNoCartao([parcela.Id], new CartaoTokenizado("tok-1", "visa", 3), noCartao),
            Ct
        );

        // Assert
        pago.Valor.ShouldBe(SituacaoDoCartao.Pago);
        _reservada!.Status.ShouldBe(StatusDaCobrancaBancaria.Paga);
        _reservada.AcrescimoEmCentavos.ShouldBe(noCartao - ValorDaParcela);
        await _mercadoPago
            .Received(1)
            .Emitir(
                "token",
                Arg.Is<PedidoDeCobranca>(p =>
                    p.Meio == MeioDePagamento.Cartao && p.ValorEmCentavos == noCartao && p.Cartao!.Token == "tok-1" && p.Cartao.Parcelas == 3
                ),
                Arg.Any<CancellationToken>()
            );
        await _recebimentos
            .Received(1)
            .Adicionar(
                Arg.Is<Recebimento>(r => r.Forma == FormaDePagamento.Cartao && r.ValorEmCentavos == ValorDaParcela),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>Sprint 39: o valor que a tela mostrou não é mais o do dia — nada é cobrado.</summary>
    [Fact]
    public async Task Cartao_com_valor_diferente_do_dia_nao_cobra()
    {
        var parcela = DaAna(_hoje.AddDays(5));
        PrepararMercadoPago(cartaoLigado: true);

        var pago = await Servico.PagarNoCartao(
            FormaturaId,
            Ana.UsuarioId,
            new PagamentoNoCartao([parcela.Id], new CartaoTokenizado("tok-1", "visa", 1), 1),
            Ct
        );

        pago.PrimeiroErro.Codigo.ShouldBe("pagamento.valor_mudou");
        await _mercadoPago.DidNotReceiveWithAnyArgs().Emitir(default!, default!, Ct);
    }

    /// <summary>Sprint 39, P7: com o cartão desligado, o pagamento é recusado antes de cobrar.</summary>
    [Fact]
    public async Task Cartao_desligado_recusa_o_pagamento()
    {
        var parcela = DaAna(_hoje.AddDays(5));
        PrepararMercadoPago();

        var pago = await Servico.PagarNoCartao(
            FormaturaId,
            Ana.UsuarioId,
            new PagamentoNoCartao([parcela.Id], new CartaoTokenizado("tok-1", "visa", 1), ValorDaParcela),
            Ct
        );

        pago.PrimeiroErro.Codigo.ShouldBe("pagamento.cartao_desligado");
    }

    /// <summary>Parcela de outro formando não se paga no cartão de ninguém: 404, como no aviso.</summary>
    [Fact]
    public async Task Cartao_na_parcela_de_outro_formando_e_404()
    {
        var parcela = DaAna(_hoje.AddDays(5));
        PrepararMercadoPago(cartaoLigado: true);

        var pago = await Servico.PagarNoCartao(
            FormaturaId,
            Bruno.UsuarioId,
            new PagamentoNoCartao([parcela.Id], new CartaoTokenizado("tok-1", "visa", 1), ValorDaParcela),
            Ct
        );

        pago.PrimeiroErro.Codigo.ShouldBe(ErrosDePagamento.ParcelaNaoEncontrada.Codigo);
    }

    /// <summary>Forma do cartão: mais de 12 vezes não chega ao Mercado Pago (P3).</summary>
    [Fact]
    public async Task Cartao_em_mais_de_12_vezes_e_recusado_pela_forma()
    {
        var parcela = DaAna(_hoje.AddDays(5));

        var pago = await Servico.PagarNoCartao(
            FormaturaId,
            Ana.UsuarioId,
            new PagamentoNoCartao([parcela.Id], new CartaoTokenizado("tok-1", "visa", 13), 100),
            Ct
        );

        pago.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Validacao);
    }

    /// <summary>O valor do dia de <see cref="DaAna"/> antes do vencimento: sem multa, juros nem desconto.</summary>
    private const long ValorDaParcela = 350_000;

    /// <summary>A cobrança que a emissão reservou, capturada para o teste conferir o status.</summary>
    private CobrancaBancaria? _reservada;

    /// <summary>Turma conectada ao Mercado Pago, com a reserva de emissão aceita e devolvida rastreada.</summary>
    /// <param name="cartaoLigado">Liga o cartão.</param>
    /// <param name="taxaRepassada">Taxa do cartão repassada.</param>
    /// <param name="automatica">Cobrança automática; falso: conectada, mas no modo manual.</param>
    private void PrepararMercadoPago(bool cartaoLigado = false, int? taxaRepassada = null, bool automatica = true)
    {
        var credencial = new CredencialDeProvedor();
        credencial.Conectar("token", "renovacao", DateTime.UtcNow.AddDays(100), 1, "turma@mp.dev", Ana.UsuarioId, "APP_USR-publica");
        credencial.DefinirCobrancaAutomatica(automatica, DateTime.UtcNow);
        if (cartaoLigado)
            credencial.LigarCartao(taxaRepassada, Ana.UsuarioId, DateTime.UtcNow);
        _provedor.ObterCredencial(Arg.Any<CancellationToken>()).Returns(credencial);
        _perfis.ObterTitular(FormaturaId, Ana.UsuarioId, Arg.Any<CancellationToken>()).Returns(Ana);
        _provedor.ReservarEmissao(Arg.Do<CobrancaBancaria>(c => _reservada = c), Arg.Any<CancellationToken>()).Returns(true);
        _provedor.ObterCobrancaParaEdicao(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => _reservada);
    }
}
