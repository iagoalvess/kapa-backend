using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Services;
using Backend.Business.MercadoPago.Interfaces;
using Backend.Business.MercadoPago.Models;
using Backend.Business.MercadoPago.Settings;
using Backend.Business.Pagamentos.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Assinaturas;

/// <summary>
/// A licença na conta do Kapa no Mercado Pago (Sprint 37): o que sai para o Mercado Pago, e como cada aviso vira o
/// vocabulário que o ciclo da assinatura entende.
/// </summary>
public sealed class ProvedorMercadoPagoTests
{
    private const string Token = "APP_USR-kapa";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IMercadoPago _mercadoPago = Substitute.For<IMercadoPago>();

    private ProvedorMercadoPago Provedor => new(_mercadoPago, Options.Create(new MercadoPagoSettings { AccessTokenDoKapa = Token }));

    private static PedidoDeCheckout Pedido(Guid? cobranca = null, CicloDeCobranca ciclo = CicloDeCobranca.Mensal) =>
        new(
            Guid.CreateVersion7(),
            "premium",
            "Premium",
            4990,
            ciclo,
            "https://app.kapa/assinatura/retorno",
            MeioDePagamento.Pix,
            "p@turma.dev",
            cobranca
        );

    [Fact]
    public async Task Cobranca_avulsa_vai_para_a_pagina_do_mercado_pago_com_o_token_do_kapa()
    {
        // Arrange
        var cobranca = Guid.CreateVersion7();
        _mercadoPago
            .CriarPagamentoAvulso(Token, Arg.Any<PedidoDePagamentoAvulso>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new PaginaDePagamento("pref-1", "https://mp/pref-1")));

        // Act
        var sessao = await Provedor.CriarCheckout(Pedido(cobranca), Ct);

        // Assert
        sessao.Valor.ShouldBe(new SessaoDeCheckout("pref-1", "https://mp/pref-1"));
        await _mercadoPago
            .Received(1)
            .CriarPagamentoAvulso(
                Token,
                Arg.Is<PedidoDePagamentoAvulso>(p => p.Referencia == cobranca && p.Meio == MeioDePagamento.Pix && p.ValorEmCentavos == 4990),
                Arg.Any<CancellationToken>()
            );
        await _mercadoPago.DidNotReceiveWithAnyArgs().CriarRecorrencia(default!, default!, Ct);
    }

    /// <summary>P2: o anual no cartão é uma recorrência de 12 em 12 meses, à vista.</summary>
    [Fact]
    public async Task Anual_no_cartao_e_recorrencia_de_doze_meses()
    {
        _mercadoPago
            .CriarRecorrencia(Token, Arg.Any<PedidoDeRecorrencia>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new RecorrenciaNoMercadoPago("PRE1", null, SituacaoDaRecorrencia.Pendente, "https://mp/PRE1", null)));
        var pedido = Pedido(ciclo: CicloDeCobranca.Anual);

        var sessao = await Provedor.CriarCheckout(pedido, Ct);

        sessao.Valor.ShouldBe(new SessaoDeCheckout("PRE1", "https://mp/PRE1"));
        await _mercadoPago
            .Received(1)
            .CriarRecorrencia(
                Token,
                Arg.Is<PedidoDeRecorrencia>(p => p.MesesPorCiclo == 12 && p.Referencia == pedido.AssinaturaId && p.EmailDoPagador == "p@turma.dev"),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>O pagamento que a recorrência gerou chega também como débito: pelo aviso <c>payment</c>, nada.</summary>
    [Fact]
    public async Task Aviso_de_pagamento_da_recorrencia_e_ignorado()
    {
        _mercadoPago
            .ConsultarPagamento(Token, "8001", Arg.Any<CancellationToken>())
            .Returns(
                Result.Ok(
                    new PagamentoNoMercadoPago("8001", Guid.CreateVersion7().ToString("N"), SituacaoDoPagamento.Aprovado, 4990, DateTime.UtcNow, true)
                )
            );

        var evento = await Provedor.Traduzir("payment", "8001", Ct);

        evento.Valor.ShouldBeNull();
    }

    [Fact]
    public async Task Pagamento_avulso_aprovado_confirma_a_cobranca()
    {
        // Arrange
        var cobranca = Guid.CreateVersion7();
        var pagoEm = DateTime.UtcNow;
        _mercadoPago
            .ConsultarPagamento(Token, "9001", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new PagamentoNoMercadoPago("9001", cobranca.ToString("N"), SituacaoDoPagamento.Aprovado, 2990, pagoEm, false)));

        // Act
        var evento = await Provedor.Traduzir("payment", "9001", Ct);

        // Assert
        evento.Valor.ShouldBe(
            new EventoDoProvedor("mp_pagamento_9001", TiposDeEvento.PagamentoConfirmado, null, null, pagoEm, cobranca, "9001", 2990)
        );
    }

    /// <summary>
    /// P3: a recusa vira um evento por débito, não por tentativa — o Presidente é avisado na primeira, e as
    /// retentativas do Mercado Pago no mesmo débito não repetem o e-mail.
    /// </summary>
    [Fact]
    public async Task Debito_recusado_vira_um_evento_por_debito()
    {
        // Arrange
        var assinatura = Guid.CreateVersion7();
        _mercadoPago
            .ConsultarDebitoDaRecorrencia(Token, "7001", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new DebitoDaRecorrencia("7001", "PRE1", "8001", SituacaoDoPagamento.Recusado, 2990, null)));
        _mercadoPago
            .ConsultarRecorrencia(Token, "PRE1", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new RecorrenciaNoMercadoPago("PRE1", assinatura.ToString("N"), SituacaoDaRecorrencia.Autorizada, null, null)));

        // Act
        var evento = await Provedor.Traduzir("subscription_authorized_payment", "7001", Ct);

        // Assert
        evento.Valor!.Id.ShouldBe("mp_recusa_7001");
        evento.Valor.Tipo.ShouldBe(TiposDeEvento.PagamentoRecusado);
        evento.Valor.AssinaturaId.ShouldBe(assinatura);
    }

    /// <summary>O débito aprovado e o aviso <c>payment</c> dele dariam o mesmo id: o índice único aplica um só.</summary>
    [Fact]
    public async Task Debito_aprovado_leva_o_id_do_pagamento()
    {
        var assinatura = Guid.CreateVersion7();
        _mercadoPago
            .ConsultarDebitoDaRecorrencia(Token, "7002", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new DebitoDaRecorrencia("7002", "PRE1", "8002", SituacaoDoPagamento.Aprovado, 4990, null)));
        _mercadoPago
            .ConsultarRecorrencia(Token, "PRE1", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new RecorrenciaNoMercadoPago("PRE1", assinatura.ToString("N"), SituacaoDaRecorrencia.Autorizada, null, null)));

        var evento = (await Provedor.Traduzir("subscription_authorized_payment", "7002", Ct)).Valor!;

        evento.Id.ShouldBe("mp_pagamento_8002");
        evento.IdDoPagamento.ShouldBe("8002");
        evento.IdExternoDaAssinatura.ShouldBe("PRE1");
        evento.ValorEmCentavos.ShouldBe(4990);
    }

    [Theory]
    [InlineData(SituacaoDaRecorrencia.Autorizada, TiposDeEvento.RecorrenciaAutorizada)]
    [InlineData(SituacaoDaRecorrencia.Cancelada, TiposDeEvento.AssinaturaCancelada)]
    [InlineData(SituacaoDaRecorrencia.Pendente, null)]
    public async Task Recorrencia_avisada_vira_o_evento_da_situacao(SituacaoDaRecorrencia situacao, string? tipo)
    {
        _mercadoPago
            .ConsultarRecorrencia(Token, "PRE1", Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new RecorrenciaNoMercadoPago("PRE1", Guid.CreateVersion7().ToString("N"), situacao, null, null)));

        var evento = await Provedor.Traduzir("subscription_preapproval", "PRE1", Ct);

        evento.Valor?.Tipo.ShouldBe(tipo);
    }

    /// <summary>Mercado Pago fora do ar na consulta: indisponível, e o recebedor responde 503 para ele reentregar.</summary>
    [Fact]
    public async Task Consulta_indisponivel_devolve_falha()
    {
        _mercadoPago
            .ConsultarPagamento(Token, "9001", Arg.Any<CancellationToken>())
            .Returns(Result.Falha<PagamentoNoMercadoPago>(Erro.Indisponivel("recebimento.provedor_indisponivel", "Fora.")));

        var evento = await Provedor.Traduzir("payment", "9001", Ct);

        evento.Falhou.ShouldBeTrue();
    }

    /// <summary>
    /// O aviso cujo recurso o Mercado Pago diz não existir responde sucesso — reentregar não o faria existir; só o
    /// Mercado Pago fora do ar pede nova entrega.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Aviso_so_pede_reentrega_quando_o_mercado_pago_esta_fora(bool indisponivel, bool sucesso)
    {
        // Arrange
        var erro = indisponivel
            ? Erro.Indisponivel("recebimento.provedor_indisponivel", "Fora.")
            : Erro.Conflito("recebimento.provedor_recusou", "Não.");
        _mercadoPago.ConsultarPagamento(Token, "404", Arg.Any<CancellationToken>()).Returns(Result.Falha<PagamentoNoMercadoPago>(erro));
        var webhook = Substitute.For<IWebhookService>();
        var avisos = new AvisosDaContaDoKapa(Provedor, webhook, NullLogger<AvisosDaContaDoKapa>.Instance);

        // Act
        var resultado = await avisos.Receber("payment", "404", Ct);

        // Assert
        resultado.Sucesso.ShouldBe(sucesso);
        await webhook.DidNotReceiveWithAnyArgs().Aplicar(default!, Ct);
    }
}
