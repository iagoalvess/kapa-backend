using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Business.MercadoPago.Models;
using Backend.Business.MercadoPago.Services;
using Backend.Business.MercadoPago.Settings;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Backend.UnitTests.Recebimentos;

/// <summary>
/// O cliente do Mercado Pago sobre um <see cref="HttpMessageHandler"/> de mentira, e o <c>state</c> do OAuth:
/// o que vai na emissão, como o pedido pago é lido, a assinatura do aviso e o <c>state</c> que não se forja.
/// </summary>
public sealed class ClienteDoMercadoPagoTests
{
    private const string Segredo = "segredo-do-webhook";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ClienteDoMercadoPago Cliente(Func<HttpRequestMessage, HttpResponseMessage> responder, List<HttpRequestMessage>? pedidos = null) =>
        new(
            new HttpClient(new Rede(responder, pedidos ?? [])),
            Options.Create(
                new MercadoPagoSettings
                {
                    ClientId = "123",
                    ClientSecret = "cs",
                    SegredoDoWebhook = Segredo,
                    UrlDeRetorno = "https://kapa.dev/retorno",
                }
            ),
            NullLogger<ClienteDoMercadoPago>.Instance
        );

    /// <summary>
    /// Decisão 12a: a referência da cobrança vai como chave de idempotência e referência externa. A validade
    /// sai em minutos inteiros — com fração de segundo o Mercado Pago responde 400.
    /// </summary>
    [Fact]
    public async Task Emissao_leva_a_referencia_como_idempotencia_e_devolve_o_qr()
    {
        // Arrange
        var pedidos = new List<HttpRequestMessage>();
        string? corpo = null;
        var cliente = Cliente(
            requisicao =>
            {
                corpo = requisicao.Content!.ReadAsStringAsync().Result;
                return Json("""{"id":"ORD1","transactions":{"payments":[{"payment_method":{"id":"pix","qr_code":"00020126qr"}}]}}""");
            },
            pedidos
        );
        var referencia = Guid.CreateVersion7();

        // Act
        var emitido = await cliente.Emitir(
            "token",
            new PedidoDeCobranca(
                referencia,
                MeioDePagamento.Pix,
                12345,
                new PagadorNoMercadoPago("ana@turma.dev"),
                TimeSpan.FromHours(3) + TimeSpan.FromSeconds(53.1234)
            ),
            Ct
        );

        // Assert
        emitido.Valor.ShouldBe(new DocumentoEmitido("ORD1", "00020126qr"));
        var enviado = pedidos.ShouldHaveSingleItem();
        enviado.RequestUri!.ToString().ShouldBe("https://api.mercadopago.com/v1/orders");
        enviado.Headers.GetValues("X-Idempotency-Key").ShouldBe([referencia.ToString("N")]);
        using var json = JsonDocument.Parse(corpo!);
        json.RootElement.GetProperty("external_reference").GetString().ShouldBe(referencia.ToString("N"));
        json.RootElement.GetProperty("total_amount").GetString().ShouldBe("123.45");
        var pagamento = json.RootElement.GetProperty("transactions").GetProperty("payments")[0];
        pagamento.GetProperty("payment_method").GetProperty("id").GetString().ShouldBe("pix");
        pagamento.GetProperty("expiration_time").GetString().ShouldBe("PT3H");
    }

    /// <summary>
    /// Sprint 35: o cartão vai com o token do SDK, a bandeira e as parcelas — o número nunca passa pelo Kapa —,
    /// sem validade, e volta já decidido: aprovado é pago, recusado explica.
    /// </summary>
    [Theory]
    [InlineData("processed", "accredited", null)]
    [InlineData("failed", "rejected_by_issuer", "pagamento.cartao_recusado")]
    public async Task Cartao_vai_tokenizado_e_volta_decidido(string status, string detalhe, string? erro)
    {
        // Arrange
        string? corpo = null;
        var cliente = Cliente(requisicao =>
        {
            corpo = requisicao.Content!.ReadAsStringAsync().Result;
            return Json($$"""{"id":"ORD2","status":"{{status}}","status_detail":"{{detalhe}}"}""");
        });

        // Act
        var emitido = await cliente.Emitir(
            "token",
            new PedidoDeCobranca(
                Guid.CreateVersion7(),
                MeioDePagamento.Cartao,
                5000,
                new PagadorNoMercadoPago("ana@turma.dev"),
                TimeSpan.Zero,
                new CartaoTokenizado("tok_123", "master", 3)
            ),
            Ct
        );

        // Assert
        if (erro is null)
            emitido.Valor.ShouldBe(new DocumentoEmitido("ORD2", null, SituacaoDoPedido.Pago));
        else
            emitido.PrimeiroErro.Codigo.ShouldBe(erro);

        using var json = JsonDocument.Parse(corpo!);
        var pagamento = json.RootElement.GetProperty("transactions").GetProperty("payments")[0];
        var metodo = pagamento.GetProperty("payment_method");
        metodo.GetProperty("id").GetString().ShouldBe("master");
        metodo.GetProperty("type").GetString().ShouldBe("credit_card");
        metodo.GetProperty("token").GetString().ShouldBe("tok_123");
        metodo.GetProperty("installments").GetInt32().ShouldBe(3);
        pagamento.TryGetProperty("expiration_time", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Sprint 35: a recorrência (<c>preapproval</c>) leva valor e ciclo no próprio pedido, e volta com a página
    /// onde a pessoa cadastra o cartão.
    /// </summary>
    [Fact]
    public async Task Recorrencia_leva_valor_e_ciclo_e_devolve_a_pagina_de_autorizar()
    {
        // Arrange
        var pedidos = new List<HttpRequestMessage>();
        string? corpo = null;
        var referencia = Guid.CreateVersion7();
        var cliente = Cliente(
            requisicao =>
            {
                corpo = requisicao.Content!.ReadAsStringAsync().Result;
                return Json(
                    $$"""{"id":"PRE1","external_reference":"{{referencia:N}}","status":"pending","init_point":"https://mp/autorizar/PRE1"}"""
                );
            },
            pedidos
        );

        // Act
        var criada = await cliente.CriarRecorrencia(
            "token",
            new PedidoDeRecorrencia(referencia, "Kapa — plano Essencial", 2990, "presidente@turma.dev", 1, "https://app.kapa/assinatura"),
            Ct
        );

        // Assert
        criada.Valor.ShouldBe(
            new RecorrenciaNoMercadoPago("PRE1", referencia.ToString("N"), SituacaoDaRecorrencia.Pendente, "https://mp/autorizar/PRE1", null)
        );
        pedidos.ShouldHaveSingleItem().RequestUri!.ToString().ShouldBe("https://api.mercadopago.com/preapproval");
        using var json = JsonDocument.Parse(corpo!);
        json.RootElement.GetProperty("payer_email").GetString().ShouldBe("presidente@turma.dev");
        var ciclo = json.RootElement.GetProperty("auto_recurring");
        ciclo.GetProperty("frequency").GetInt32().ShouldBe(1);
        ciclo.GetProperty("frequency_type").GetString().ShouldBe("months");
        ciclo.GetProperty("transaction_amount").GetDecimal().ShouldBe(29.90m);
        ciclo.GetProperty("currency_id").GetString().ShouldBe("BRL");
    }

    [Theory]
    [InlineData("authorized", SituacaoDaRecorrencia.Autorizada)]
    [InlineData("paused", SituacaoDaRecorrencia.Pausada)]
    [InlineData("cancelled", SituacaoDaRecorrencia.Cancelada)]
    [InlineData("algo_novo", SituacaoDaRecorrencia.Pendente)]
    public async Task Recorrencia_consultada_traduz_a_situacao(string status, SituacaoDaRecorrencia situacao)
    {
        var cliente = Cliente(_ => Json($$"""{"id":"PRE1","status":"{{status}}","next_payment_date":"2026-10-25T12:00:00.000-03:00"}"""));

        var lida = await cliente.ConsultarRecorrencia("token", "PRE1", Ct);

        lida.Valor.Situacao.ShouldBe(situacao);
        lida.Valor.ProximaCobrancaEm.ShouldBe(new DateTime(2026, 10, 25, 15, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("processed", "accredited", SituacaoDoPedido.Pago, 5000)]
    [InlineData("action_required", "waiting_transfer", SituacaoDoPedido.Aberto, 0)]
    [InlineData("expired", "expired", SituacaoDoPedido.Encerrado, 0)]
    [InlineData("refunded", "refunded", SituacaoDoPedido.Devolvido, 0)]
    [InlineData("charged_back", "in_process", SituacaoDoPedido.Contestado, 0)]
    [InlineData("processed", "partially_refunded", SituacaoDoPedido.Aberto, 0)]
    public async Task Pedido_consultado_diz_se_foi_pago_e_quanto(string status, string detalhe, SituacaoDoPedido situacao, long pago)
    {
        // Arrange
        var cliente = Cliente(_ =>
            Json(
                $$"""{"id":"ORD1","external_reference":"abc","status":"{{status}}","status_detail":"{{detalhe}}","total_amount":"50.00","total_paid_amount":"50.00","last_updated_date":"2026-09-24T20:50:41.511Z"}"""
            )
        );

        // Act
        var pedido = await cliente.ConsultarPedido("token", "ORD1", Ct);

        // Assert
        pedido.Valor.Situacao.ShouldBe(situacao);
        pedido.Valor.Referencia.ShouldBe("abc");
        pedido.Valor.ValorPagoEmCentavos.ShouldBe(pago);
    }

    [Fact]
    public async Task Cartao_parcelado_baixa_o_valor_do_pedido_sem_os_juros_do_comprador()
    {
        // Arrange
        var cliente = Cliente(_ =>
            Json(
                """{"id":"ORD1","external_reference":"abc","status":"processed","status_detail":"accredited","total_amount":"100.00","total_paid_amount":"111.23"}"""
            )
        );

        // Act
        var pedido = await cliente.ConsultarPedido("token", "ORD1", Ct);

        // Assert
        pedido.Valor.ValorPagoEmCentavos.ShouldBe(10_000);
    }

    [Fact]
    public async Task Pagamento_buscado_traz_o_liquido_e_a_tarifa()
    {
        // Arrange
        var cliente = Cliente(_ =>
            Json(
                """{"results":[{"id":181392479300,"external_reference":"abc","status":"approved","transaction_amount":100,"transaction_details":{"net_received_amount":95.02}}]}"""
            )
        );

        // Act
        var pagamento = await cliente.BuscarPagamentoAprovado("token", Guid.CreateVersion7(), Ct);

        // Assert
        pagamento.Valor!.LiquidoEmCentavos.ShouldBe(9_502);
        pagamento.Valor.TarifaEmCentavos.ShouldBe(498);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "recebimento.autorizacao_recusada")]
    [InlineData(HttpStatusCode.InternalServerError, "recebimento.provedor_indisponivel")]
    public async Task Erro_do_mercado_pago_vira_codigo_do_kapa(HttpStatusCode status, string codigo)
    {
        var cliente = Cliente(_ => new HttpResponseMessage(status));

        var pedido = await cliente.ConsultarPedido("token", "ORD1", Ct);

        pedido.PrimeiroErro.Codigo.ShouldBe(codigo);
    }

    /// <summary>Decisão 12: o aviso só é aceito com a assinatura do segredo da aplicação — e o id vai em minúsculas.</summary>
    [Fact]
    public void Aviso_so_e_autentico_com_a_assinatura_do_segredo()
    {
        var cliente = Cliente(_ => throw new InvalidOperationException("sem rede"));
        var v1 = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Segredo), Encoding.UTF8.GetBytes("id:ord1;request-id:req-9;ts:1704908010;"))
        );

        cliente.AvisoAutentico($"ts=1704908010,v1={v1}", "req-9", "ORD1").ShouldBeTrue();
        cliente.AvisoAutentico($"ts=1704908010,v1={v1}", "req-9", "ORD2").ShouldBeFalse();
        cliente.AvisoAutentico($"ts=1704908011,v1={v1}", "req-9", "ORD1").ShouldBeFalse();
        cliente.AvisoAutentico("ts=1704908010,v1=naoehex", "req-9", "ORD1").ShouldBeFalse();
        cliente.AvisoAutentico(null, "req-9", "ORD1").ShouldBeFalse();
    }

    [Fact]
    public void Url_de_autorizacao_leva_o_client_id_o_state_e_o_retorno()
    {
        var url = Cliente(_ => throw new InvalidOperationException()).UrlDeAutorizacao("a.b");

        url.ShouldBe(
            "https://auth.mercadopago.com.br/authorization?client_id=123&response_type=code&platform_id=mp&state=a.b&redirect_uri=https%3A%2F%2Fkapa.dev%2Fretorno"
        );
    }

    /// <summary>
    /// A troca de tokens pede token de produção: a API de Orders recusa credencial de teste, inclusive no
    /// sandbox — lá quem cobra é um usuário de teste com credencial de produção.
    /// </summary>
    [Fact]
    public async Task Troca_de_tokens_pede_token_de_producao()
    {
        string? corpo = null;
        var cliente = Cliente(requisicao =>
        {
            corpo = requisicao.Content!.ReadAsStringAsync().Result;
            return Json("""{"access_token":"at","refresh_token":"rt","user_id":7,"expires_in":15552000}""");
        });

        var tokens = await cliente.Autorizar("codigo", Ct);

        tokens.Valor.AccessToken.ShouldBe("at");
        using var json = JsonDocument.Parse(corpo!);
        json.RootElement.GetProperty("grant_type").GetString().ShouldBe("authorization_code");
        json.RootElement.TryGetProperty("test_token", out _).ShouldBeFalse();
    }

    /// <summary>O retorno do OAuth chega sem sessão: só o <c>state</c> íntegro e no prazo diz de quem ele é.</summary>
    [Fact]
    public void State_do_oauth_volta_a_turma_e_nao_aceita_forja_nem_atraso()
    {
        var formaturaId = Guid.CreateVersion7();
        var usuarioId = Guid.CreateVersion7();
        var agora = DateTime.UtcNow;
        var state = EstadoDaConexao.Assinar(formaturaId, usuarioId, agora, "cs");

        EstadoDaConexao.Ler(state, agora, "cs").ShouldBe((formaturaId, usuarioId));
        EstadoDaConexao.Ler(state, agora, "outro-segredo").ShouldBeNull();
        EstadoDaConexao.Ler(state, agora + EstadoDaConexao.Validade + TimeSpan.FromSeconds(1), "cs").ShouldBeNull();
        EstadoDaConexao.Ler((state[0] == 'A' ? 'B' : 'A') + state[1..], agora, "cs").ShouldBeNull();
        EstadoDaConexao.Ler("lixo", agora, "cs").ShouldBeNull();
        EstadoDaConexao.Ler(null, agora, "cs").ShouldBeNull();
    }

    /// <summary>
    /// Sprint 37: a página avulsa do PIX fecha cartão, boleto e saldo em conta; a referência externa é a cobrança do
    /// Kapa, e o endereço devolvido é o <c>init_point</c>.
    /// </summary>
    [Fact]
    public async Task Pagina_avulsa_no_pix_fecha_os_outros_meios()
    {
        // Arrange
        var pedidos = new List<HttpRequestMessage>();
        string? corpo = null;
        var cliente = Cliente(
            requisicao =>
            {
                corpo = requisicao.Content!.ReadAsStringAsync().Result;
                return Json("""{"id":"123-abc","init_point":"https://mp/checkout/123-abc"}""");
            },
            pedidos
        );
        var referencia = Guid.CreateVersion7();

        // Act
        var pagina = await cliente.CriarPagamentoAvulso(
            "token",
            new PedidoDePagamentoAvulso(
                referencia,
                "Kapa — plano Essencial",
                2990,
                MeioDePagamento.Pix,
                null,
                "https://app.kapa/assinatura/retorno",
                new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc)
            ),
            Ct
        );

        // Assert
        pagina.Valor.ShouldBe(new PaginaDePagamento("123-abc", "https://mp/checkout/123-abc"));
        pedidos.ShouldHaveSingleItem().RequestUri!.ToString().ShouldBe("https://api.mercadopago.com/checkout/preferences");
        using var json = JsonDocument.Parse(corpo!);
        json.RootElement.GetProperty("external_reference").GetString().ShouldBe(referencia.ToString("N"));
        json.RootElement.GetProperty("items")[0].GetProperty("unit_price").GetDecimal().ShouldBe(29.90m);
        json.RootElement.GetProperty("expiration_date_to").GetString().ShouldBe("2026-10-05T12:00:00.000Z");
        json.RootElement.TryGetProperty("payer", out _).ShouldBeFalse();
        var excluidos = json
            .RootElement.GetProperty("payment_methods")
            .GetProperty("excluded_payment_types")
            .EnumerateArray()
            .Select(tipo => tipo.GetProperty("id").GetString())
            .ToList();
        excluidos.ShouldContain("credit_card");
        excluidos.ShouldContain("ticket");
        excluidos.ShouldNotContain("bank_transfer");
    }

    /// <summary>O pagamento que a recorrência gerou é marcado — o aviso dele chega também como débito.</summary>
    [Theory]
    [InlineData("approved", "recurring_payment", SituacaoDoPagamento.Aprovado, true)]
    [InlineData("rejected", "regular_payment", SituacaoDoPagamento.Recusado, false)]
    [InlineData("refunded", "regular_payment", SituacaoDoPagamento.Devolvido, false)]
    [InlineData("in_process", "regular_payment", SituacaoDoPagamento.Pendente, false)]
    public async Task Pagamento_consultado_traduz_a_situacao(string status, string operacao, SituacaoDoPagamento situacao, bool daRecorrencia)
    {
        var cliente = Cliente(_ =>
            Json(
                $$"""{"id":123,"status":"{{status}}","operation_type":"{{operacao}}","external_reference":"ref","transaction_amount":49.9,"date_approved":"2026-09-28T10:00:00.000-03:00"}"""
            )
        );

        var lido = await cliente.ConsultarPagamento("token", "123", Ct);

        lido.Valor.Situacao.ShouldBe(situacao);
        lido.Valor.DaRecorrencia.ShouldBe(daRecorrencia);
        lido.Valor.ValorEmCentavos.ShouldBe(4990);
        lido.Valor.Id.ShouldBe("123");
    }

    [Fact]
    public async Task Debito_da_recorrencia_traz_o_pagamento_e_a_recorrencia()
    {
        var cliente = Cliente(_ =>
            Json(
                """{"id":7001,"preapproval_id":"PRE1","transaction_amount":29.9,"last_modified":"2026-09-28T10:00:00.000-03:00","payment":{"id":8001,"status":"rejected"}}"""
            )
        );

        var debito = await cliente.ConsultarDebitoDaRecorrencia("token", "7001", Ct);

        debito.Valor.ShouldBe(
            new DebitoDaRecorrencia("7001", "PRE1", "8001", SituacaoDoPagamento.Recusado, 2990, new DateTime(2026, 9, 28, 13, 0, 0, DateTimeKind.Utc))
        );
    }

    /// <summary>O estorno leva o valor em reais e a chave de idempotência: a nova tentativa não devolve duas vezes.</summary>
    [Fact]
    public async Task Estorno_leva_o_valor_e_a_idempotencia()
    {
        var pedidos = new List<HttpRequestMessage>();
        string? corpo = null;
        var cliente = Cliente(
            requisicao =>
            {
                corpo = requisicao.Content!.ReadAsStringAsync().Result;
                return Json("""{"id":1,"status":"approved"}""");
            },
            pedidos
        );
        var chave = Guid.CreateVersion7();

        var estorno = await cliente.Estornar("token", "8001", 1495, chave, Ct);

        estorno.Sucesso.ShouldBeTrue();
        var pedido = pedidos.ShouldHaveSingleItem();
        pedido.RequestUri!.ToString().ShouldBe("https://api.mercadopago.com/v1/payments/8001/refunds");
        pedido.Headers.GetValues("X-Idempotency-Key").ShouldHaveSingleItem().ShouldBe(chave.ToString("N"));
        JsonDocument.Parse(corpo!).RootElement.GetProperty("amount").GetDecimal().ShouldBe(14.95m);
    }

    /// <summary>A troca de meio (P5): a recorrência nova começa no fim da vigência.</summary>
    [Fact]
    public async Task Recorrencia_com_inicio_leva_a_data_do_primeiro_debito()
    {
        string? corpo = null;
        var cliente = Cliente(requisicao =>
        {
            corpo = requisicao.Content!.ReadAsStringAsync().Result;
            return Json("""{"id":"PRE2","status":"pending","init_point":"https://mp/autorizar/PRE2"}""");
        });

        await cliente.CriarRecorrencia(
            "token",
            new PedidoDeRecorrencia(
                Guid.CreateVersion7(),
                "Kapa",
                2990,
                "p@turma.dev",
                12,
                "https://app",
                new DateTime(2026, 10, 28, 15, 0, 0, DateTimeKind.Utc)
            ),
            Ct
        );

        var ciclo = JsonDocument.Parse(corpo!).RootElement.GetProperty("auto_recurring");
        ciclo.GetProperty("frequency").GetInt32().ShouldBe(12);
        ciclo.GetProperty("start_date").GetString().ShouldBe("2026-10-28T15:00:00.000Z");
    }

    private static HttpResponseMessage Json(string corpo) =>
        new(HttpStatusCode.OK) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };

    /// <summary>A rede de mentira: guarda o que saiu e responde o que o teste mandar.</summary>
    private sealed class Rede(Func<HttpRequestMessage, HttpResponseMessage> responder, List<HttpRequestMessage> pedidos) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            pedidos.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
