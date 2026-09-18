using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Pagamentos.Services;
using Backend.Business.Pagamentos.Validators;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
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
    }

    private PagamentoService Servico
    {
        get
        {
            var emails = new EmailsDePagamento(_email, Options.Create(new AplicacaoSettings()));

            return new PagamentoService(
                _parcelas,
                _informes,
                _recebimentos,
                _contas,
                _perfis,
                _vinculos,
                _formaturas,
                _arquivos,
                new BaixaService(_recebimentos, _eventos, emails),
                emails,
                _eventos,
                new NovoInformeValidator(),
                new BaixaManualValidator(),
                new ConfirmarInformesValidator(),
                new RecusarInformeValidator(),
                new EstornarBaixaValidator(),
                _unitOfWork,
                NullLogger<PagamentoService>.Instance
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

    private static ContaDeRecebimentoDetalhe Conta(bool conferida) =>
        new(TipoDeChavePix.Cpf, "52998224725", "Comissão Medicina", "Curitiba", DateTime.UtcNow, conferida ? DateTime.UtcNow : null, null);

    [Fact]
    public async Task Informe_grava_pendente_e_nao_mexe_na_parcela()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));

        // Act
        var resultado = await Servico.Informar(FormaturaId, Ana.UsuarioId, [parcela.Id], new NovoInforme(_hoje, 350_000), null, Ct);

        // Assert
        resultado.Valor[0].Status.ShouldBe(StatusDaParcela.Aberta);
        resultado.Valor[0].EmConferencia.ShouldBeTrue();
        await _informes
            .Received(1)
            .Adicionar(
                Arg.Is<InformeDePagamento>(i => i.ParcelaId == parcela.Id && i.Status == StatusDoInforme.Pendente && i.VinculoId == Ana.VinculoId),
                Ct
            );
        await _parcelas.DidNotReceiveWithAnyArgs().TravarParaBaixa(default!, Ct);
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    [Fact]
    public async Task Parcela_de_outro_formando_e_404_no_informe_no_pix_e_na_leitura()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));

        // Act
        var informe = await Servico.Informar(FormaturaId, Bruno.UsuarioId, [parcela.Id], new NovoInforme(_hoje, 350_000), null, Ct);
        var pix = await Servico.GerarPix(FormaturaId, Bruno.UsuarioId, parcela.Id, Ct);
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
        var pixDaComissao = await Servico.GerarPix(FormaturaId, Comissao.UsuarioId, parcela.Id, Ct);
        var pixDaTesouraria = await Servico.GerarPix(FormaturaId, Tesoureira.UsuarioId, parcela.Id, Ct);

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
        var segundo = await Servico.Informar(FormaturaId, Ana.UsuarioId, [emConferencia.Id], new NovoInforme(_hoje, 350_000), null, Ct);
        var naPaga = await Servico.Informar(FormaturaId, Ana.UsuarioId, [paga.Id], new NovoInforme(_hoje, 350_000), null, Ct);

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
            new NovoInforme(_hoje, 800_000),
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
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    /// <summary>Uma parcela recusada derruba o aviso inteiro: nada é gravado pela metade.</summary>
    [Fact]
    public async Task Parcela_ja_paga_no_meio_do_lote_recusa_o_aviso_inteiro()
    {
        // Arrange
        var aberta = DaAna(_hoje.AddDays(-5));
        var paga = DaAna(_hoje.AddDays(-35), StatusDaParcela.Paga);

        // Act
        var resultado = await Servico.Informar(FormaturaId, Ana.UsuarioId, [aberta.Id, paga.Id], new NovoInforme(_hoje, 700_000), null, Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("pagamento.parcela_paga");
        await _informes.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Aviso_sem_parcela_nenhuma_e_recusado()
    {
        // Act
        var resultado = await Servico.Informar(FormaturaId, Ana.UsuarioId, [], new NovoInforme(_hoje, 350_000), null, Ct);

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
        var resultado = await Servico.Informar(FormaturaId, Ana.UsuarioId, [parcela.Id], new NovoInforme(_hoje.AddDays(1), 350_000), null, Ct);

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
        var comConta = await Servico.GerarPix(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);
        var semConta = await Servico.GerarPix(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        comConta.Valor.CopiaECola.ShouldContain("52998224725");
        comConta.Valor.CopiaECola.ShouldContain(comConta.Valor.Identificador);
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
        var pix = await Servico.GerarPixDeVarias(FormaturaId, Ana.UsuarioId, [aVencer.Id, vencida.Id], Ct);

        // Assert
        pix.Valor.ValorEmCentavos.ShouldBe(350_000 + 7_000 + 3_500 + 350_000);
        pix.Valor.Identificador.ShouldBe(PagamentoService.Identificador(vencida.Id));
        pix.Valor.CopiaECola.ShouldContain("54077105.00");
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
        var vazio = await Servico.GerarPixDeVarias(FormaturaId, Ana.UsuarioId, [], Ct);
        var deOutro = await Servico.GerarPixDeVarias(FormaturaId, Bruno.UsuarioId, [daAna.Id], Ct);
        var comAvisada = await Servico.GerarPixDeVarias(FormaturaId, Ana.UsuarioId, [daAna.Id, avisada.Id], Ct);

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
        var pix = await Servico.GerarPix(FormaturaId, Ana.UsuarioId, parcela.Id, Ct);

        // Assert
        pix.Valor.ValorEmCentavos.ShouldBe(350_000 + 7_000 + 3_500);
        pix.Valor.CopiaECola.ShouldContain("54073605.00");
    }

    [Fact]
    public async Task Recusa_exige_motivo()
    {
        // Act
        var resultado = await Servico.Recusar(FormaturaId, Tesoureira.UsuarioId, Guid.CreateVersion7(), new RecusarInforme("  "), Ct);

        // Assert
        resultado.PrimeiroErro.Campo.ShouldBe("motivo");
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Recusa_grava_o_motivo_e_avisa_o_formando_sem_mexer_na_parcela()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5));
        var informe = InformeDePagamento.Novo(parcela.Id, Ana.VinculoId, _hoje, 350_000, null);
        _informes.ListarParaEdicao(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([informe]);

        // Act
        var resultado = await Servico.Recusar(
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
        await _parcelas.DidNotReceiveWithAnyArgs().TravarParaBaixa(default!, Ct);
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    /// <summary>A baixa grava tudo no mesmo lote: parcela, recebimento com o devido, informe, e-mail e auditoria.</summary>
    [Fact]
    public async Task Confirmar_baixa_a_parcela_com_o_recebido_e_guarda_o_devido_do_dia()
    {
        // Arrange
        var parcela = Parcela.Nova(Ana.VinculoId, Guid.CreateVersion7(), new ParcelaPrevista(3, _hoje.AddDays(-30), 350_000));
        var informe = InformeDePagamento.Novo(parcela.Id, Ana.VinculoId, _hoje, 350_000, null);
        Preparar(parcela, informe);

        // Act
        var resultado = await Servico.Confirmar(
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
                    && r.Divergente
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
        var informe = InformeDePagamento.Novo(parcela.Id, Ana.VinculoId, _hoje, 350_000, null);
        informe.Confirmar(Tesoureira.UsuarioId, DateTime.UtcNow);
        Preparar(parcela, informe);

        // Act
        var resultado = await Servico.Confirmar(
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
        var resultado = await Servico.Confirmar(
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
        var resultado = await Servico.Estornar(FormaturaId, Tesoureira.UsuarioId, null, parcela.Id, new EstornarBaixa("Dinheiro devolvido."), Ct);

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
        await _eventos.Received(1).Adicionar(Arg.Is<Evento>(e => e.Nome == PagamentoService.EventoDeEstorno), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Baixa_manual_de_parcela_com_aviso_pendente_manda_para_a_conferencia()
    {
        // Arrange
        var parcela = DaAna(_hoje.AddDays(5), emConferencia: true);

        // Act
        var resultado = await Servico.BaixarManualmente(
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
}
