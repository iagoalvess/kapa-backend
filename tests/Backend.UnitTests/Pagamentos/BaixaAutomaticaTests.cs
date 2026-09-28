using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Loja.Interfaces;
using Backend.Business.Loja.Models;
using Backend.Business.MercadoPago.Interfaces;
using Backend.Business.MercadoPago.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Pagamentos.Services;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Backend.UnitTests.Loja;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Pagamentos;

/// <summary>
/// A baixa que chega pelo Mercado Pago (Sprint 25, Parte C): o valor vem da consulta, a cobrança baixa
/// uma vez, e o que não confere não baixa.
/// </summary>
public sealed class BaixaAutomaticaTests
{
    private static readonly Guid VinculoId = Guid.CreateVersion7();
    private static readonly Guid PresidenteId = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IProvedorDaTurmaRepository _provedor = Substitute.For<IProvedorDaTurmaRepository>();
    private readonly IMercadoPago _mercadoPago = Substitute.For<IMercadoPago>();
    private readonly IParcelaRepository _parcelas = Substitute.For<IParcelaRepository>();
    private readonly IInformeRepository _informes = Substitute.For<IInformeRepository>();
    private readonly IRecebimentoRepository _recebimentos = Substitute.For<IRecebimentoRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IEventoRepository _eventos = Substitute.For<IEventoRepository>();
    private readonly ICompraDeConviteRepository _compras = Substitute.For<ICompraDeConviteRepository>();

    private readonly DateOnly _hoje = DataUtils.Hoje();
    private readonly Parcela _antiga;
    private readonly Parcela _nova;
    private CobrancaBancaria _cobranca;

    public BaixaAutomaticaTests()
    {
        _antiga = Parcela.Nova(VinculoId, Guid.CreateVersion7(), new ParcelaPrevista(1, _hoje.AddDays(5), 30_000));
        _nova = Parcela.Nova(VinculoId, Guid.CreateVersion7(), new ParcelaPrevista(2, _hoje.AddDays(35), 30_000));
        _cobranca = Cobranca(MeioDePagamento.Pix);

        var credencial = new CredencialDeProvedor();
        credencial.Conectar("token", "renovacao", DateTime.UtcNow.AddDays(100), 1, "turma@mp.dev", PresidenteId);

        _provedor.ObterCredencial(Arg.Any<CancellationToken>()).Returns(credencial);
        _provedor.ObterCobranca(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => _cobranca);
        _provedor.TravarCobranca(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => _cobranca);
        _parcelas.TravarParaBaixa(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([_nova, _antiga]);
        _parcelas
            .ObterRegrasDeAtraso(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, RegrasDeAtraso>());
        _informes.ListarPendentesParaEdicao(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result<bool>>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<Result<bool>>>>()(Ct));
    }

    private BaixaAutomatica Baixa =>
        new(
            _provedor,
            _mercadoPago,
            _parcelas,
            _informes,
            Substitute.For<IVinculoRepository>(),
            Substitute.For<IFormaturaRepository>(),
            new BaixaService(
                _recebimentos,
                _eventos,
                new EmailsDePagamento(Substitute.For<IEmailService>(), Options.Create(new AplicacaoSettings())),
                Substitute.For<IQuitacaoDePedidos>()
            ),
            LojaTests.Pagamento(_compras),
            _eventos,
            _unitOfWork,
            NullLogger<BaixaAutomatica>.Instance
        );

    private CobrancaBancaria Cobranca(MeioDePagamento meio)
    {
        var cobranca = new CobrancaBancaria(meio, 1, [_antiga.Id, _nova.Id], 60_000, DateTime.UtcNow.AddHours(3), "pix:1:as-duas");
        cobranca.Emitida("ORD1", "qr");

        return cobranca;
    }

    private void Pago(long centavos, string? referencia = null) =>
        _mercadoPago
            .ConsultarPedido("token", "ORD1", Arg.Any<CancellationToken>())
            .Returns(
                Result.Ok(new PedidoConsultado("ORD1", referencia ?? _cobranca.Id.ToString("N"), SituacaoDoPedido.Pago, centavos, DateTime.UtcNow))
            );

    /// <summary>Decisão 10: um pagamento, várias parcelas — da mais antiga para a mais nova, pelo devido de cada uma.</summary>
    [Fact]
    public async Task Pedido_pago_baixa_as_parcelas_da_mais_antiga_para_a_mais_nova()
    {
        // Arrange
        Pago(60_000);

        // Act
        var baixou = await Baixa.Conciliar(_cobranca.Id, Ct);

        // Assert
        baixou.Valor.ShouldBeTrue();
        _antiga.Status.ShouldBe(StatusDaParcela.Paga);
        _nova.Status.ShouldBe(StatusDaParcela.Paga);
        _cobranca.Status.ShouldBe(StatusDaCobrancaBancaria.Paga);
        await _recebimentos
            .Received(1)
            .Adicionar(
                Arg.Is<Recebimento>(r => r.ParcelaId == _antiga.Id && r.ValorEmCentavos == 30_000 && r.BaixadoPorUsuarioId == PresidenteId),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>Sprint 26: a cobrança paga de uma compra da loja confirma a compra e não toca em parcela nenhuma.</summary>
    [Fact]
    public async Task Cobranca_da_loja_paga_confirma_a_compra_sem_baixar_parcela()
    {
        // Arrange
        var compra = LojaTests.Compra();
        _compras.Travar(compra.Id, Arg.Any<CancellationToken>()).Returns(compra);
        _cobranca = CobrancaBancaria.DaCompra(MeioDePagamento.Pix, 1, compra.Id, 40_000, DateTime.UtcNow.AddMinutes(30));
        _cobranca.Emitida("ORD1", "qr");
        Pago(40_000);

        // Act
        var confirmou = await Baixa.Conciliar(_cobranca.Id, Ct);

        // Assert
        confirmou.Valor.ShouldBeTrue();
        compra.Status.ShouldBe(StatusDaCompra.Paga);
        _cobranca.Status.ShouldBe(StatusDaCobrancaBancaria.Paga);
        await _parcelas.DidNotReceiveWithAnyArgs().TravarParaBaixa(default!, Ct);
        await _recebimentos.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>Decisão 11: aviso e conciliação do mesmo pagamento — a segunda passada encontra a cobrança paga.</summary>
    [Fact]
    public async Task Segunda_conciliacao_do_mesmo_pagamento_nao_baixa_de_novo()
    {
        // Arrange
        Pago(60_000);
        await Baixa.Conciliar(_cobranca.Id, Ct);

        // Act
        var segunda = await Baixa.Conciliar(_cobranca.Id, Ct);

        // Assert
        segunda.Valor.ShouldBeFalse();
        await _recebimentos.Received(2).Adicionar(Arg.Any<Recebimento>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Decisão 12: pedido pago que não é o desta cobrança não baixa nada.</summary>
    [Fact]
    public async Task Pedido_com_referencia_de_outra_cobranca_nao_baixa()
    {
        Pago(60_000, referencia: Guid.CreateVersion7().ToString("N"));

        var baixou = await Baixa.Conciliar(_cobranca.Id, Ct);

        baixou.Valor.ShouldBeFalse();
        _antiga.Status.ShouldBe(StatusDaParcela.Aberta);
        await _recebimentos.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>Pedido vencido lá sai da conciliação, e a parcela continua em aberto.</summary>
    [Fact]
    public async Task Pedido_encerrado_encerra_a_cobranca_sem_baixar()
    {
        _mercadoPago
            .ConsultarPedido("token", "ORD1", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new PedidoConsultado("ORD1", _cobranca.Id.ToString("N"), SituacaoDoPedido.Encerrado, 0, null)));

        var baixou = await Baixa.Conciliar(_cobranca.Id, Ct);

        baixou.Valor.ShouldBeFalse();
        _cobranca.Status.ShouldBe(StatusDaCobrancaBancaria.Encerrada);
        _antiga.Status.ShouldBe(StatusDaParcela.Aberta);
    }

    /// <summary>Aviso pendente do formando sobre a parcela paga é confirmado junto, e sai da fila.</summary>
    [Fact]
    public async Task Aviso_pendente_da_parcela_e_confirmado_pela_baixa()
    {
        // Arrange
        Pago(60_000);
        var informe = InformeDePagamento.Novo(_antiga.Id, VinculoId, _hoje, 30_000, null, MeioDeRecebimento.Pix);
        _informes.ListarPendentesParaEdicao(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([informe]);

        // Act
        await Baixa.Conciliar(_cobranca.Id, Ct);

        // Assert
        informe.Status.ShouldBe(StatusDoInforme.Confirmado);
    }

    /// <summary>Sprint 35: a forma da baixa vem do meio da cobrança — o cartão baixa como cartão, o PIX como PIX.</summary>
    [Theory]
    [InlineData(MeioDePagamento.Pix, FormaDePagamento.Pix)]
    [InlineData(MeioDePagamento.Cartao, FormaDePagamento.Cartao)]
    [InlineData(MeioDePagamento.PixAutomatico, FormaDePagamento.Pix)]
    public async Task A_baixa_grava_a_forma_do_meio_da_cobranca(MeioDePagamento meio, FormaDePagamento forma)
    {
        // Arrange
        _cobranca = Cobranca(meio);
        Pago(60_000);

        // Act
        await Baixa.Conciliar(_cobranca.Id, Ct);

        // Assert
        await _recebimentos.Received(2).Adicionar(Arg.Is<Recebimento>(r => r.Forma == forma), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Troca de conta (P3): o pedido da conta anterior não se consulta com o token da nova. Vencido, sai da
    /// conciliação — em vez de a rodada falhar a cada dois minutos para sempre.
    /// </summary>
    [Fact]
    public async Task Cobranca_de_outra_conta_nao_e_consultada_e_sai_ao_vencer()
    {
        // Arrange
        var credencial = new CredencialDeProvedor();
        credencial.Conectar("token-da-nova", "renovacao", DateTime.UtcNow.AddDays(100), 2, "nova@mp.dev", PresidenteId);
        _provedor.ObterCredencial(Arg.Any<CancellationToken>()).Returns(credencial);
        _cobranca = new CobrancaBancaria(MeioDePagamento.Pix, 1, [_antiga.Id], 30_000, DateTime.UtcNow.AddHours(-1), "pix:1:antiga");
        _cobranca.Emitida("ORD1", "qr");

        // Act
        var baixou = await Baixa.Conciliar(_cobranca.Id, Ct);

        // Assert
        baixou.Valor.ShouldBeFalse();
        _cobranca.Status.ShouldBe(StatusDaCobrancaBancaria.Encerrada);
        await _mercadoPago.DidNotReceiveWithAnyArgs().ConsultarPedido(default!, default!, Ct);
    }
}
