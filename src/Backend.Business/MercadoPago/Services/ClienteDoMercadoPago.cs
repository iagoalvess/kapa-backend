using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Backend.Business.Abstractions;
using Backend.Business.MercadoPago.Interfaces;
using Backend.Business.MercadoPago.Models;
using Backend.Business.MercadoPago.Settings;
using Backend.Business.Pagamentos.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business.MercadoPago.Services;

/// <summary>
/// O cliente da API do Mercado Pago: OAuth, a conta dona do token, o PIX e o cartão pela API de Orders,
/// a recorrência pela de assinaturas e a assinatura dos avisos.
/// </summary>
/// <remarks>
/// A API de Orders, e não a de Payments: é a que a documentação indica hoje, e a aplicação do Kapa foi
/// criada nela (Sprint 25, conferência de 23/09/2026). O <c>X-Idempotency-Key</c> é obrigatório lá, e é
/// a referência da cobrança no Kapa — a mesma chave em nova tentativa devolve o mesmo pedido (decisão 12a).
/// <para>
/// Token e segredo nunca vão para log nem para mensagem de erro: o log diz o status HTTP e a referência,
/// e mais nada.
/// </para>
/// </remarks>
/// <param name="http">Cliente HTTP compartilhado.</param>
/// <param name="options">A aplicação do Kapa e a API.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ClienteDoMercadoPago(HttpClient http, IOptions<MercadoPagoSettings> options, ILogger<ClienteDoMercadoPago> logger) : IMercadoPago
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly Erro Indisponivel = Erro.Indisponivel(
        "recebimento.provedor_indisponivel",
        "O Mercado Pago não respondeu agora. Tente de novo em alguns minutos."
    );

    /// <summary>
    /// O Mercado Pago respondeu que não (4xx): o pedido não existe lá, e tentar de novo com a mesma chave não
    /// muda nada. O resto — tempo esgotado, rede, 5xx, 408 e 429 — é resultado incerto: o pedido pode ter
    /// nascido, e quem chama retoma com a mesma chave (Sprint 25, decisão 12a).
    /// </summary>
    private static readonly Erro Recusado = Erro.Conflito(
        "recebimento.provedor_recusou",
        "O Mercado Pago recusou este pagamento. Tente outro meio ou fale com a comissão."
    );

    private static bool Recusou(HttpStatusCode status) =>
        (int)status is >= 400 and < 500 && status is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests);

    private static readonly Erro CartaoRecusado = Erro.Conflito(
        "pagamento.cartao_recusado",
        "O cartão foi recusado. Confira os dados, tente outro cartão ou pague pelo PIX."
    );

    private readonly MercadoPagoSettings _config = options.Value;

    /// <inheritdoc />
    public string UrlDeAutorizacao(string state) =>
        $"{_config.UrlDeAutorizacao}?client_id={Uri.EscapeDataString(_config.ClientId)}&response_type=code&platform_id=mp"
        + $"&state={Uri.EscapeDataString(state)}&redirect_uri={Uri.EscapeDataString(_config.UrlDeRetorno)}";

    /// <inheritdoc />
    public Task<Result<TokensDoMercadoPago>> Autorizar(string codigo, CancellationToken ct = default) =>
        PedirTokens(new PedidoDeToken(_config.ClientId, _config.ClientSecret, "authorization_code", codigo, _config.UrlDeRetorno, null), ct);

    /// <inheritdoc />
    public Task<Result<TokensDoMercadoPago>> Renovar(string refreshToken, CancellationToken ct = default) =>
        PedirTokens(new PedidoDeToken(_config.ClientId, _config.ClientSecret, "refresh_token", null, null, refreshToken), ct);

    /// <inheritdoc />
    /// <remarks>
    /// Conta de fora do Brasil é recusada: o PIX só existe aqui, e uma conta argentina aceitaria a
    /// autorização e nunca emitiria cobrança nenhuma.
    /// </remarks>
    public async Task<Result<ContaNoMercadoPago>> ConsultarConta(string accessToken, CancellationToken ct = default)
    {
        using var requisicao = Requisicao(HttpMethod.Get, "users/me", accessToken);

        var usuario = await Ler<Usuario>(requisicao, "consulta da conta", ct);
        if (usuario.Falhou)
            return Result.Falha<ContaNoMercadoPago>(usuario.Erros);

        var dados = usuario.Valor;

        if (dados.SiteId != "MLB")
            return Erro.Conflito(
                "recebimento.conta_fora_do_brasil",
                "Esta conta do Mercado Pago não é do Brasil. Conecte a conta brasileira da turma."
            );

        return new ContaNoMercadoPago(
            dados.Id,
            string.IsNullOrWhiteSpace(dados.Email) ? dados.Nickname ?? dados.Id.ToString(CultureInfo.InvariantCulture) : dados.Email,
            dados.Tags?.Contains("test_user") == true
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// O pedido leva o e-mail e, quando quem chama sabe, o nome — a loja pública o manda, para a turma
    /// reconhecer o comprador no painel (Sprint 26); CPF, nunca. O PIX volta com o <c>qr_code</c> e nasce
    /// aberto. O cartão vai com o token que o SDK gerou no navegador e volta já decidido: aprovado é pago,
    /// recusado é <c>pagamento.cartao_recusado</c>, e o número do cartão nunca passou por aqui.
    /// </remarks>
    public async Task<Result<DocumentoEmitido>> Emitir(string accessToken, PedidoDeCobranca pedido, CancellationToken ct = default)
    {
        var valor = Reais(pedido.ValorEmCentavos);
        var referencia = pedido.Referencia.ToString("N");
        var cartao = pedido.Meio == MeioDePagamento.Cartao ? pedido.Cartao : null;
        var pagador = pedido.Pagador;

        if (pedido.Meio == MeioDePagamento.Cartao && cartao is null)
            throw new ArgumentException("Cartão sem o token que o SDK gera no navegador.", nameof(pedido));

        using var requisicao = Requisicao(HttpMethod.Post, "v1/orders", accessToken);
        requisicao.Headers.Add("X-Idempotency-Key", referencia);
        requisicao.Content = JsonContent.Create(
            new Pedido(
                null,
                "online",
                referencia,
                valor,
                null,
                null,
                null,
                new Pagador(pagador.Email, pagador.Nome, pagador.Sobrenome, null),
                new Transacoes([
                    new Pagamento(
                        valor,
                        null,
                        null,
                        cartao is null
                            ? new MetodoDePagamento("pix", "bank_transfer", null)
                            : new MetodoDePagamento(cartao.Bandeira, "credit_card", null) { Token = cartao.Token, Installments = cartao.Parcelas },
                        cartao is null ? Duracao(pedido.Validade) : null
                    ),
                ])
            ),
            options: Json
        );

        var emitido = await Ler<Pedido>(requisicao, $"{pedido.Meio} da cobrança {referencia}", ct);
        if (emitido.Falhou)
            return cartao is not null && emitido.PrimeiroErro.Codigo == Recusado.Codigo
                ? CartaoRecusado
                : Result.Falha<DocumentoEmitido>(emitido.Erros);

        if (emitido.Valor.Status == "failed")
            return cartao is null ? Indisponivel : CartaoRecusado;

        if (emitido.Valor.Id is not { } id)
        {
            logger.LogWarning("Mercado Pago devolveu a cobrança {Referencia} sem id.", referencia);
            return Indisponivel;
        }

        if (cartao is not null)
            return new DocumentoEmitido(id, null, Situacao(emitido.Valor));

        var qrCode = emitido.Valor.Transactions?.Payments is [var pagamento, ..] ? pagamento.PaymentMethod?.QrCode : null;

        if (string.IsNullOrWhiteSpace(qrCode))
        {
            logger.LogWarning("Mercado Pago devolveu o PIX da cobrança {Referencia} sem o copia-e-cola.", referencia);
            return Indisponivel;
        }

        return new DocumentoEmitido(id, qrCode);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pela API de assinaturas (<c>preapproval</c>), sem plano associado: o valor e o ciclo vão no próprio
    /// pedido, e quem paga cadastra o cartão na página do Mercado Pago (<c>init_point</c>).
    /// </remarks>
    public async Task<Result<RecorrenciaNoMercadoPago>> CriarRecorrencia(
        string accessToken,
        PedidoDeRecorrencia pedido,
        CancellationToken ct = default
    )
    {
        using var requisicao = Requisicao(HttpMethod.Post, "preapproval", accessToken);
        requisicao.Content = JsonContent.Create(
            new Recorrencia(
                null,
                pedido.Motivo,
                pedido.Referencia.ToString("N"),
                pedido.EmailDoPagador,
                new CicloDaRecorrencia(pedido.MesesPorCiclo, "months", pedido.ValorEmCentavos / 100m, "BRL") { StartDate = Data(pedido.ComecaEm) },
                pedido.UrlDeRetorno,
                "pending",
                null,
                null
            ),
            options: Json
        );

        return (await Ler<Recorrencia>(requisicao, $"recorrência {pedido.Referencia:N}", ct)).Map(ParaRecorrencia);
    }

    /// <inheritdoc />
    public async Task<Result<RecorrenciaNoMercadoPago>> ConsultarRecorrencia(string accessToken, string idExterno, CancellationToken ct = default)
    {
        using var requisicao = Requisicao(HttpMethod.Get, $"preapproval/{Uri.EscapeDataString(idExterno)}", accessToken);

        return (await Ler<Recorrencia>(requisicao, $"consulta da recorrência {idExterno}", ct)).Map(ParaRecorrencia);
    }

    /// <inheritdoc />
    public async Task<Result> CancelarRecorrencia(string accessToken, string idExterno, CancellationToken ct = default)
    {
        using var requisicao = Requisicao(HttpMethod.Put, $"preapproval/{Uri.EscapeDataString(idExterno)}", accessToken);
        requisicao.Content = JsonContent.Create(new { status = "cancelled" }, options: Json);

        var cancelada = await Ler<Recorrencia>(requisicao, $"cancelamento da recorrência {idExterno}", ct);

        return cancelada.Falhou ? Result.Falha(cancelada.Erros) : Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result> AtualizarValorDaRecorrencia(string accessToken, string idExterno, long valorEmCentavos, CancellationToken ct = default)
    {
        using var requisicao = Requisicao(HttpMethod.Put, $"preapproval/{Uri.EscapeDataString(idExterno)}", accessToken);
        requisicao.Content = JsonContent.Create(
            new { AutoRecurring = new { TransactionAmount = valorEmCentavos / 100m, CurrencyId = "BRL" } },
            options: Json
        );

        var atualizada = await Ler<Recorrencia>(requisicao, $"valor da recorrência {idExterno}", ct);

        return atualizada.Falhou ? Result.Falha(atualizada.Erros) : Result.Ok();
    }

    /// <inheritdoc />
    /// <remarks>
    /// O débito sem tentativa ainda (<c>payment</c> nulo) é pendente. O que o Mercado Pago ainda vai retentar
    /// (<c>recycling</c>) chega aqui como recusado se a tentativa foi recusada — é o aviso da primeira recusa (P3).
    /// </remarks>
    public async Task<Result<DebitoDaRecorrencia>> ConsultarDebitoDaRecorrencia(string accessToken, string idExterno, CancellationToken ct = default)
    {
        using var requisicao = Requisicao(HttpMethod.Get, $"authorized_payments/{Uri.EscapeDataString(idExterno)}", accessToken);

        var lido = await Ler<Debito>(requisicao, $"consulta do débito {idExterno}", ct);
        if (lido.Falhou)
            return Result.Falha<DebitoDaRecorrencia>(lido.Erros);

        var debito = lido.Valor;

        return new DebitoDaRecorrencia(
            debito.Id?.ToString(CultureInfo.InvariantCulture) ?? idExterno,
            debito.PreapprovalId,
            debito.Payment?.Id?.ToString(CultureInfo.InvariantCulture),
            debito.Payment is { } pagamento ? SituacaoDoPagamentoDe(pagamento.Status) : SituacaoDoPagamento.Pendente,
            (long)Math.Round((debito.TransactionAmount ?? 0) * 100m),
            (debito.LastModified ?? debito.DebitDate)?.ToUniversalTime()
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// Checkout Pro (<c>checkout/preferences</c>): a página do Mercado Pago, com os meios fechados no que o Kapa
    /// pediu — só PIX, ou só cartão de crédito em uma vez; boleto fica de fora (Sprint 35). O saldo em conta não sai: o
    /// Checkout Pro responde 400 <c>account_money cannot be excluded</c> (sandbox, 28/09/2026) — e ele cai na conta do
    /// Kapa como qualquer outro meio. A referência externa é o id da cobrança no Kapa, que volta no pagamento.
    /// </remarks>
    public async Task<Result<PaginaDePagamento>> CriarPagamentoAvulso(
        string accessToken,
        PedidoDePagamentoAvulso pedido,
        CancellationToken ct = default
    )
    {
        var referencia = pedido.Referencia.ToString("N");
        string[] excluidos =
            pedido.Meio == MeioDePagamento.Pix
                ? ["credit_card", "debit_card", "prepaid_card", "ticket", "atm"]
                : ["debit_card", "prepaid_card", "ticket", "atm", "bank_transfer"];

        using var requisicao = Requisicao(HttpMethod.Post, "checkout/preferences", accessToken);
        requisicao.Headers.Add("X-Idempotency-Key", referencia);
        requisicao.Content = JsonContent.Create(
            new
            {
                Items = new[] { new ItemDaPagina(referencia, pedido.Titulo, 1, pedido.ValorEmCentavos / 100m, "BRL") },
                Payer = pedido.EmailDoPagador is { } email ? new { Email = email } : null,
                ExternalReference = referencia,
                BackUrls = new
                {
                    Success = pedido.UrlDeRetorno,
                    Pending = pedido.UrlDeRetorno,
                    Failure = pedido.UrlDeRetorno,
                },
                PaymentMethods = new { ExcludedPaymentTypes = excluidos.Select(id => new { Id = id }), Installments = 1 },
                Expires = true,
                ExpirationDateTo = Data(pedido.Validade),
                StatementDescriptor = "KAPA",
            },
            options: Json
        );

        var criada = await Ler<Preferencia>(requisicao, $"página de pagamento {referencia}", ct);
        if (criada.Falhou)
            return Result.Falha<PaginaDePagamento>(criada.Erros);

        if (criada.Valor is not { Id: { } id, InitPoint: { } url })
        {
            logger.LogWarning("Mercado Pago devolveu a página de pagamento {Referencia} sem id ou endereço.", referencia);
            return Indisponivel;
        }

        return new PaginaDePagamento(id, url);
    }

    /// <inheritdoc />
    public async Task<Result<PagamentoNoMercadoPago>> ConsultarPagamento(string accessToken, string idExterno, CancellationToken ct = default)
    {
        using var requisicao = Requisicao(HttpMethod.Get, $"v1/payments/{Uri.EscapeDataString(idExterno)}", accessToken);

        return (await Ler<PagamentoLido>(requisicao, $"consulta do pagamento {idExterno}", ct)).Map(ParaPagamento);
    }

    /// <inheritdoc />
    public async Task<Result<PagamentoNoMercadoPago?>> BuscarPagamentoAprovado(string accessToken, Guid referencia, CancellationToken ct = default)
    {
        using var requisicao = Requisicao(
            HttpMethod.Get,
            $"v1/payments/search?external_reference={referencia:N}&status=approved&sort=date_created&criteria=desc&limit=1",
            accessToken
        );

        var busca = await Ler<BuscaDePagamentos>(requisicao, $"busca do pagamento {referencia:N}", ct);
        if (busca.Falhou)
            return Result.Falha<PagamentoNoMercadoPago?>(busca.Erros);

        return busca.Valor.Results is [var primeiro, ..] ? ParaPagamento(primeiro) : Result.Ok<PagamentoNoMercadoPago?>(null);
    }

    /// <inheritdoc />
    public async Task<Result> Estornar(string accessToken, string idDoPagamento, long valorEmCentavos, Guid chave, CancellationToken ct = default)
    {
        using var requisicao = Requisicao(HttpMethod.Post, $"v1/payments/{Uri.EscapeDataString(idDoPagamento)}/refunds", accessToken);
        requisicao.Headers.Add("X-Idempotency-Key", chave.ToString("N"));
        requisicao.Content = JsonContent.Create(new { Amount = valorEmCentavos / 100m }, options: Json);

        var estorno = await Ler<Devolucao>(requisicao, $"estorno do pagamento {idDoPagamento}", ct);

        return estorno.Falhou ? Result.Falha(estorno.Erros) : Result.Ok();
    }

    private static PagamentoNoMercadoPago ParaPagamento(PagamentoLido pagamento) =>
        new(
            pagamento.Id.ToString(CultureInfo.InvariantCulture),
            pagamento.ExternalReference,
            SituacaoDoPagamentoDe(pagamento.Status),
            (long)Math.Round((pagamento.TransactionAmount ?? 0) * 100m),
            pagamento.DateApproved?.ToUniversalTime(),
            pagamento.OperationType == "recurring_payment"
        );

    /// <summary>O <c>status</c> de um pagamento como o Kapa o entende; o que não é conhecido fica pendente.</summary>
    private static SituacaoDoPagamento SituacaoDoPagamentoDe(string? status) =>
        status switch
        {
            "approved" => SituacaoDoPagamento.Aprovado,
            "rejected" or "cancelled" => SituacaoDoPagamento.Recusado,
            "refunded" or "charged_back" => SituacaoDoPagamento.Devolvido,
            _ => SituacaoDoPagamento.Pendente,
        };

    /// <summary>A data como as APIs de assinatura e de preferência escrevem: ISO 8601 com milissegundos, em UTC.</summary>
    private static string? Data(DateTime? utc) => utc?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public async Task<Result<PedidoConsultado>> ConsultarPedido(string accessToken, string idExterno, CancellationToken ct = default)
    {
        using var requisicao = Requisicao(HttpMethod.Get, $"v1/orders/{Uri.EscapeDataString(idExterno)}", accessToken);

        var lido = await Ler<Pedido>(requisicao, $"consulta do pedido {idExterno}", ct);
        if (lido.Falhou)
            return Result.Falha<PedidoConsultado>(lido.Erros);

        var pedido = lido.Valor;
        var situacao = Situacao(pedido);

        return new PedidoConsultado(
            pedido.Id ?? idExterno,
            pedido.ExternalReference,
            situacao,
            situacao == SituacaoDoPedido.Pago ? Centavos(pedido.TotalPaidAmount) : 0,
            situacao == SituacaoDoPedido.Pago ? pedido.LastUpdatedDate?.ToUniversalTime() : null,
            pedido.Payer?.Identification is { Type: "CPF", Number: { } cpf } ? cpf : null
        );
    }

    /// <summary>
    /// Pago é <c>processed</c> com <c>accredited</c> — o dinheiro creditado, não só aprovado. O que não vai
    /// mais ser pago (vencido, cancelado, recusado, devolvido) é encerrado; o resto segue aberto.
    /// </summary>
    private static SituacaoDoPedido Situacao(Pedido pedido) =>
        (pedido.Status, pedido.StatusDetail) switch
        {
            ("processed", "accredited") => SituacaoDoPedido.Pago,
            ("expired" or "canceled" or "failed" or "refunded" or "charged_back", _) => SituacaoDoPedido.Encerrado,
            _ => SituacaoDoPedido.Aberto,
        };

    /// <summary>
    /// A recorrência como o Kapa a entende. O que o Mercado Pago não documenta como estado conhecido fica
    /// pendente — nunca autorizada por engano.
    /// </summary>
    private static RecorrenciaNoMercadoPago ParaRecorrencia(Recorrencia recorrencia) =>
        new(
            recorrencia.Id ?? string.Empty,
            recorrencia.ExternalReference,
            recorrencia.Status switch
            {
                "authorized" => SituacaoDaRecorrencia.Autorizada,
                "paused" => SituacaoDaRecorrencia.Pausada,
                "cancelled" => SituacaoDaRecorrencia.Cancelada,
                _ => SituacaoDaRecorrencia.Pendente,
            },
            recorrencia.InitPoint,
            recorrencia.NextPaymentDate?.ToUniversalTime()
        );

    /// <inheritdoc />
    /// <remarks>
    /// O manifesto é o da documentação: <c>id:{data.id};request-id:{x-request-id};ts:{ts};</c>, com o id em
    /// minúsculas, e cada parte ausente sai do texto. Comparação em tempo constante.
    /// </remarks>
    public bool AvisoAutentico(string? assinatura, string? idDaRequisicao, string? idDoRecurso)
    {
        if (string.IsNullOrWhiteSpace(_config.SegredoDoWebhook) || string.IsNullOrWhiteSpace(assinatura))
            return false;

        string? ts = null;
        string? v1 = null;

        foreach (var parte in assinatura.Split(','))
        {
            var chaveEValor = parte.Split('=', 2, StringSplitOptions.TrimEntries);
            if (chaveEValor is ["ts", var valorTs])
                ts = valorTs;
            else if (chaveEValor is ["v1", var valorV1])
                v1 = valorV1;
        }

        if (ts is null || v1 is null)
            return false;

        var manifesto = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(idDoRecurso))
            manifesto.Append("id:").Append(idDoRecurso.ToLowerInvariant()).Append(';');
        if (!string.IsNullOrWhiteSpace(idDaRequisicao))
            manifesto.Append("request-id:").Append(idDaRequisicao).Append(';');
        manifesto.Append("ts:").Append(ts).Append(';');

        var esperado = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_config.SegredoDoWebhook), Encoding.UTF8.GetBytes(manifesto.ToString()));

        try
        {
            return CryptographicOperations.FixedTimeEquals(esperado, Convert.FromHexString(v1));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task<Result<TokensDoMercadoPago>> PedirTokens(PedidoDeToken pedido, CancellationToken ct)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_config.BaseUrl), "oauth/token"))
        {
            Content = JsonContent.Create(pedido, options: Json),
        };

        var lido = await Ler<RespostaDeToken>(requisicao, "pedido de token", ct);
        if (lido.Falhou)
            return Result.Falha<TokensDoMercadoPago>(lido.Erros);

        var tokens = lido.Valor;

        if (string.IsNullOrWhiteSpace(tokens.AccessToken) || string.IsNullOrWhiteSpace(tokens.RefreshToken))
            return Indisponivel;

        return new TokensDoMercadoPago(tokens.AccessToken, tokens.RefreshToken, tokens.UserId, DateTime.UtcNow.AddSeconds(tokens.ExpiresIn));
    }

    /// <summary>
    /// Envia, lê o corpo e traduz o que der errado: recusa de credencial vira 409 que a tela explica; o
    /// resto, indisponível.
    /// </summary>
    private async Task<Result<T>> Ler<T>(HttpRequestMessage requisicao, string oQue, CancellationToken ct)
        where T : class
    {
        using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct);
        espera.CancelAfter(TimeSpan.FromSeconds(_config.SegundosDeEspera));

        try
        {
            using var resposta = await http.SendAsync(requisicao, espera.Token);

            var recusada =
                resposta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                || (
                    resposta.StatusCode == HttpStatusCode.BadRequest
                    && requisicao.RequestUri?.AbsolutePath.EndsWith("/oauth/token", StringComparison.Ordinal) == true
                );

            if (recusada)
            {
                logger.LogWarning("Mercado Pago recusou a credencial no(a) {OQue} com HTTP {Status}.", oQue, (int)resposta.StatusCode);
                return Erro.Conflito("recebimento.autorizacao_recusada", "O Mercado Pago recusou a autorização. Conecte a conta da turma de novo.");
            }

            if (!resposta.IsSuccessStatusCode)
            {
                var motivo = await resposta.Content.ReadAsStringAsync(espera.Token);
                logger.LogWarning(
                    "Mercado Pago respondeu HTTP {Status} no(a) {OQue}: {Motivo}",
                    (int)resposta.StatusCode,
                    oQue,
                    motivo.Length > 500 ? motivo[..500] : motivo
                );
                return Recusou(resposta.StatusCode) ? Recusado : Indisponivel;
            }

            return await resposta.Content.ReadFromJsonAsync<T>(Json, espera.Token) is { } lido ? lido : Indisponivel;
        }
        catch (Exception excecao)
            when (excecao is HttpRequestException or JsonException || excecao is OperationCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(excecao, "Mercado Pago não respondeu no(a) {OQue}.", oQue);
            return Indisponivel;
        }
    }

    private HttpRequestMessage Requisicao(HttpMethod metodo, string caminho, string accessToken)
    {
        var requisicao = new HttpRequestMessage(metodo, new Uri(new Uri(_config.BaseUrl), caminho));
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return requisicao;
    }

    /// <summary>
    /// A validade no formato ISO 8601 que a API aceita: em minutos inteiros. Com fração de segundo
    /// (<c>PT2H34M53.12S</c>) ela responde 400 — descoberto no sandbox em 24/09/2026.
    /// </summary>
    private static string Duracao(TimeSpan validade) => XmlConvert.ToString(TimeSpan.FromMinutes(Math.Floor(validade.TotalMinutes)));

    /// <summary>Centavos no formato de reais que a API espera: <c>"123.45"</c>.</summary>
    private static string Reais(long centavos) => (centavos / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Reais como a API escreve, em centavos; texto ausente ou torto é zero.</summary>
    private static long Centavos(string? reais) =>
        decimal.TryParse(reais, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor) ? (long)Math.Round(valor * 100m) : 0;

    private sealed record PedidoDeToken(
        string ClientId,
        string ClientSecret,
        string GrantType,
        string? Code,
        string? RedirectUri,
        string? RefreshToken
    );

    private sealed record RespostaDeToken(string? AccessToken, string? RefreshToken, long UserId, int ExpiresIn);

    private sealed record Usuario(long Id, string? Nickname, string? Email, string? SiteId, IReadOnlyList<string>? Tags);

    private sealed record Pedido(
        string? Id,
        string? Type,
        string? ExternalReference,
        string? TotalAmount,
        string? TotalPaidAmount,
        string? Status,
        string? StatusDetail,
        Pagador? Payer,
        Transacoes? Transactions
    )
    {
        public DateTime? LastUpdatedDate { get; init; }
    }

    private sealed record Pagador(string Email, string? FirstName, string? LastName, Identificacao? Identification);

    private sealed record Identificacao(string Type, string Number);

    private sealed record Transacoes(IReadOnlyList<Pagamento>? Payments);

    private sealed record Pagamento(string? Amount, string? Id, string? Status, MetodoDePagamento? PaymentMethod, string? ExpirationTime);

    private sealed record MetodoDePagamento(string? Id, string? Type, string? QrCode)
    {
        public string? Token { get; init; }

        public int? Installments { get; init; }
    }

    private sealed record Recorrencia(
        string? Id,
        string? Reason,
        string? ExternalReference,
        string? PayerEmail,
        CicloDaRecorrencia? AutoRecurring,
        string? BackUrl,
        string? Status,
        string? InitPoint,
        DateTime? NextPaymentDate
    );

    private sealed record CicloDaRecorrencia(int Frequency, string FrequencyType, decimal TransactionAmount, string CurrencyId)
    {
        public string? StartDate { get; init; }
    }

    private sealed record Debito(
        long? Id,
        string? PreapprovalId,
        decimal? TransactionAmount,
        DateTime? DebitDate,
        DateTime? LastModified,
        PagamentoDoDebito? Payment
    );

    private sealed record PagamentoDoDebito(long? Id, string? Status);

    private sealed record Preferencia(string? Id, string? InitPoint);

    private sealed record ItemDaPagina(string Id, string Title, int Quantity, decimal UnitPrice, string CurrencyId);

    private sealed record PagamentoLido(
        long Id,
        string? ExternalReference,
        string? Status,
        decimal? TransactionAmount,
        DateTime? DateApproved,
        string? OperationType
    );

    private sealed record BuscaDePagamentos(IReadOnlyList<PagamentoLido>? Results);

    private sealed record Devolucao(long? Id, string? Status);
}
