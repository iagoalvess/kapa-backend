using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
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
    private readonly IDespesaRepository _despesas = Substitute.For<IDespesaRepository>();
    private readonly IOutraReceitaRepository _receitas = Substitute.For<IOutraReceitaRepository>();
    private readonly IValorADevolverRepository _valoresADevolver = Substitute.For<IValorADevolverRepository>();
    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly ICancelamentoDaCompraService _cancelamento = Substitute.For<ICancelamentoDaCompraService>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();

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
        _mercadoPago
            .BuscarPagamentoAprovado(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok<PagamentoNoMercadoPago?>(null));
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
            _vinculos,
            Substitute.For<IFormaturaRepository>(),
            new BaixaService(
                _recebimentos,
                _eventos,
                new EmailsDePagamento(_email, Options.Create(new AplicacaoSettings())),
                Substitute.For<IQuitacaoDePedidos>(),
                new ValoresADevolver(_valoresADevolver)
            ),
            LojaTests.Pagamento(_compras, _receitas, cancelamento: _cancelamento),
            _eventos,
            _recebimentos,
            _despesas,
            _receitas,
            new EmailsDePagamento(_email, Options.Create(new AplicacaoSettings())),
            new ValoresADevolver(_valoresADevolver),
            new EstornoDaCobranca(_receitas),
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
                Arg.Is<Recebimento>(r =>
                    r.ParcelaId == _antiga.Id && r.ValorEmCentavos == 30_000 && r.BaixadoPorUsuarioId == PresidenteId && r.CobrancaId == _cobranca.Id
                ),
                Arg.Any<CancellationToken>()
            );
        await _valoresADevolver.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>
    /// Sprint 42, F7: o Mercado Pago confirma um pagamento de duas parcelas e uma delas já foi cancelada — a outra baixa,
    /// e o que era dela vai para a lista "a devolver" como pago sem parcela, em vez de só um log.
    /// </summary>
    [Fact]
    public async Task Parte_paga_sem_parcela_aberta_vai_para_a_lista_a_devolver()
    {
        // Arrange
        _nova.Cancelar(_hoje);
        Pago(60_000);
        ValorADevolver? registrado = null;
        _valoresADevolver
            .When(v => v.Adicionar(Arg.Any<ValorADevolver>(), Arg.Any<CancellationToken>()))
            .Do(c => registrado = c.Arg<ValorADevolver>());

        // Act
        var baixou = await Baixa.Conciliar(_cobranca.Id, Ct);

        // Assert
        baixou.Valor.ShouldBeTrue();
        _antiga.Status.ShouldBe(StatusDaParcela.Paga);
        registrado.ShouldNotBeNull();
        registrado.Origem.ShouldBe(OrigemDoValorADevolver.PagoSemParcela);
        registrado.ValorEmCentavos.ShouldBe(30_000);
        registrado.CobrancaId.ShouldBe(_cobranca.Id);
        registrado.ParcelaId.ShouldBe(_nova.Id);
        registrado.VinculoId.ShouldBe(VinculoId);
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

    /// <summary>Sprint 39, P6: a tarifa do Mercado Pago — o valor menos o líquido — entra no caixa como despesa paga.</summary>
    [Fact]
    public async Task Pagamento_lanca_a_tarifa_do_mercado_pago_como_despesa_paga()
    {
        // Arrange
        Pago(60_000);
        _mercadoPago
            .BuscarPagamentoAprovado("token", _cobranca.Id, Arg.Any<CancellationToken>())
            .Returns(Result.Ok<PagamentoNoMercadoPago?>(new("1", null, SituacaoDoPagamento.Aprovado, 60_000, DateTime.UtcNow, false, 57_012)));

        // Act
        await Baixa.Conciliar(_cobranca.Id, Ct);

        // Assert
        await _despesas
            .Received(1)
            .Adicionar(
                Arg.Is<IReadOnlyList<Despesa>>(d =>
                    d.Count == 1 && d[0].ValorEmCentavos == 2_988 && d[0].Status == StatusDaDespesa.Paga && d[0].Categoria == CategoriaDeDespesa.Taxas
                ),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>Sprint 39, P2: a taxa repassada não baixa parcela — as parcelas recebem o valor do dia, e o acréscimo vira receita.</summary>
    [Fact]
    public async Task Taxa_repassada_vira_receita_e_as_parcelas_recebem_sem_ela()
    {
        // Arrange
        _cobranca = CobrancaBancaria.NoCartao(1, [_antiga.Id, _nova.Id], 63_000, 3_000, "tok", DateTime.UtcNow);
        _cobranca.Emitida("ORD1", null);
        Pago(63_000);

        // Act
        await Baixa.Conciliar(_cobranca.Id, Ct);

        // Assert
        await _recebimentos.Received(2).Adicionar(Arg.Is<Recebimento>(r => r.ValorEmCentavos == 30_000), Arg.Any<CancellationToken>());
        await _receitas
            .Received(1)
            .Adicionar(
                Arg.Is<OutraReceita>(r => r.ValorEmCentavos == 3_000 && r.Status == StatusDaOutraReceita.Recebida),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>Sprint 39, P4: a cobrança paga que o pagador contestou no cartão estorna as baixas dela e avisa a comissão.</summary>
    [Fact]
    public async Task Contestacao_no_cartao_estorna_as_baixas_e_avisa_a_comissao()
    {
        // Arrange
        _cobranca = Cobranca(MeioDePagamento.Cartao);
        Pago(60_000);
        var baixas = new List<Recebimento>();
        _recebimentos.When(r => r.Adicionar(Arg.Any<Recebimento>(), Arg.Any<CancellationToken>())).Do(c => baixas.Add(c.Arg<Recebimento>()));
        await Baixa.Conciliar(_cobranca.Id, Ct);
        _recebimentos
            .ObterAtivoDaCobrancaParaEdicao(Arg.Any<Guid>(), _cobranca.Id, Arg.Any<FormaDePagamento>(), Arg.Any<CancellationToken>())
            .Returns(c => baixas.FirstOrDefault(b => b.ParcelaId == c.ArgAt<Guid>(0) && b.CobrancaId == c.ArgAt<Guid>(1)));
        _vinculos.ListarEmailsDaComissao(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(["tesouraria@kapa.dev"]);
        _mercadoPago
            .ConsultarPedido("token", "ORD1", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new PedidoConsultado("ORD1", _cobranca.Id.ToString("N"), SituacaoDoPedido.Contestado, 0, null)));

        // Act
        var estornou = await Baixa.Conciliar(_cobranca.Id, Ct);

        // Assert
        estornou.Valor.ShouldBeTrue();
        _cobranca.Status.ShouldBe(StatusDaCobrancaBancaria.Estornada);
        _antiga.Status.ShouldBe(StatusDaParcela.Aberta);
        _nova.Status.ShouldBe(StatusDaParcela.Aberta);
        baixas.ShouldAllBe(b => b.EstornadoEm != null && b.JustificativaDoEstorno == BaixaAutomatica.MotivoDaContestacao);
        await _email.Received(1).Enfileirar(Arg.Is<NovoEmail>(e => e.Para == "tesouraria@kapa.dev"), Arg.Any<CancellationToken>());
    }

    /// <summary>P4 e P6: a devolução estorna também o acréscimo repassado — o pagador recebeu o valor inteiro de volta.</summary>
    [Fact]
    public async Task Devolucao_estorna_o_acrescimo_repassado()
    {
        // Arrange
        _cobranca = CobrancaBancaria.NoCartao(1, [_antiga.Id, _nova.Id], 63_000, 3_000, "tok", DateTime.UtcNow);
        _cobranca.Emitida("ORD1", null);
        Pago(63_000);
        var lancadas = new List<OutraReceita>();
        _receitas.When(r => r.Adicionar(Arg.Any<OutraReceita>(), Arg.Any<CancellationToken>())).Do(c => lancadas.Add(c.Arg<OutraReceita>()));
        await Baixa.Conciliar(_cobranca.Id, Ct);
        _mercadoPago
            .ConsultarPedido("token", "ORD1", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new PedidoConsultado("ORD1", _cobranca.Id.ToString("N"), SituacaoDoPedido.Devolvido, 0, null)));

        // Act
        await Baixa.Conciliar(_cobranca.Id, Ct);

        // Assert
        lancadas.Select(r => r.ValorEmCentavos).ShouldBe([3_000, -3_000]);
        lancadas[1].EstornoDeId.ShouldBe(lancadas[0].Id);
        lancadas[1].Categoria.ShouldBe(CategoriaDeOutraReceita.Outros);
    }

    /// <summary>
    /// Sprint 42, F3: a baixa manual feita depois, mesmo no PIX, não é desta cobrança — a devolução procura a baixa pela
    /// cobrança, e não a mais recente da parcela.
    /// </summary>
    [Fact]
    public async Task Devolucao_nao_estorna_a_baixa_manual_feita_depois()
    {
        // Arrange
        _cobranca.Paga();
        var manual = Recebimento.Novo(
            _antiga.Id,
            null,
            new DadosDaBaixa(FormaDePagamento.Pix, _hoje, 30_000, null, PresidenteId, null, DateTime.UtcNow),
            30_000
        );
        _recebimentos.ObterAtivoParaEdicao(_antiga.Id, Arg.Any<CancellationToken>()).Returns(manual);
        _mercadoPago
            .ConsultarPedido("token", "ORD1", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new PedidoConsultado("ORD1", _cobranca.Id.ToString("N"), SituacaoDoPedido.Devolvido, 0, null)));

        // Act
        await Baixa.Conciliar(_cobranca.Id, Ct);

        // Assert
        manual.EstornadoEm.ShouldBeNull();
        _cobranca.Status.ShouldBe(StatusDaCobrancaBancaria.Estornada);
    }

    /// <summary>Sprint 39, P5: a compra da loja devolvida pelo Mercado Pago vai pelo cancelamento da Sprint 38.</summary>
    [Fact]
    public async Task Compra_da_loja_devolvida_vai_pelo_cancelamento()
    {
        // Arrange
        var compraId = Guid.CreateVersion7();
        _cobranca = CobrancaBancaria.DaCompra(MeioDePagamento.Cartao, 1, compraId, 40_000, DateTime.UtcNow.AddDays(2));
        _cobranca.Emitida("ORD1", null);
        _cobranca.Paga();
        _cancelamento
            .DevolverPeloMercadoPago(compraId, BaixaAutomatica.MotivoDaDevolucao, PresidenteId, Arg.Any<CancellationToken>())
            .Returns(Result.Ok("a compra da loja"));
        _mercadoPago
            .ConsultarPedido("token", "ORD1", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new PedidoConsultado("ORD1", _cobranca.Id.ToString("N"), SituacaoDoPedido.Devolvido, 0, null)));

        // Act
        var desfez = await Baixa.Conciliar(_cobranca.Id, Ct);

        // Assert
        desfez.Valor.ShouldBeTrue();
        await _cancelamento
            .Received(1)
            .DevolverPeloMercadoPago(compraId, BaixaAutomatica.MotivoDaDevolucao, PresidenteId, Arg.Any<CancellationToken>());
        await _parcelas.DidNotReceiveWithAnyArgs().TravarParaBaixa(default!, Ct);
    }

    /// <summary>A cobrança paga que segue paga lá não muda nada — o aviso repetido do pagamento.</summary>
    [Fact]
    public async Task Cobranca_paga_que_segue_paga_nao_faz_nada()
    {
        _cobranca.Paga();
        Pago(60_000);

        var fez = await Baixa.Conciliar(_cobranca.Id, Ct);

        fez.Valor.ShouldBeFalse();
        _cobranca.Status.ShouldBe(StatusDaCobrancaBancaria.Paga);
    }
}
