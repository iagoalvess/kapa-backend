using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Settings;
using Microsoft.Extensions.Options;

namespace Backend.Data.Provedores;

/// <summary>
/// PSP simulado: checkout, webhook assinado por HMAC e consulta, sem rede e sem credencial.
/// </summary>
/// <remarks>
/// Serve a desenvolvimento, testes de integração e demonstração comercial. A página de pagamento é
/// servida pela própria API (<c>ProvedorFakeController</c>) e, ao pagar, entrega um webhook assinado
/// com <see cref="AssinaturaSettings.SegredoDoWebhook"/> — o mesmo caminho, com a mesma verificação,
/// que o PSP real vai percorrer.
/// <para>
/// O formato do evento é o próprio <see cref="EventoDoProvedor"/> em JSON: o provedor real traduz o
/// dele para esse tipo, e o fake não tem o que traduzir.
/// </para>
/// <para>
/// ponytail: sessões em memória do processo, sem limpeza. O worker é outro processo e não enxerga
/// os pagamentos feitos na página fake — com a fake, a conciliação só acha pagamento no processo da
/// API (é onde os testes a exercitam). O PSP real é consultado por HTTP e não tem esse teto.
/// </para>
/// </remarks>
/// <param name="options">Segredo do webhook e endereço da página fake.</param>
public sealed class ProvedorFake(IOptions<AssinaturaSettings> options) : IProvedorDeAssinatura
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, SessaoFake> _sessoes = new(StringComparer.Ordinal);

    private AssinaturaSettings Settings => options.Value;

    /// <summary>Se o pagamento na página fake entrega o webhook.</summary>
    public bool EntregaWebhook => Settings.Fake.EntregarWebhook;

    /// <inheritdoc />
    public Task<Result<SessaoDeCheckout>> CriarCheckout(PedidoDeCheckout pedido, CancellationToken ct = default)
    {
        var id = $"fake_{Guid.CreateVersion7():N}";
        _sessoes[id] = new SessaoFake(id, pedido, Pagamento: null);

        var url = $"{Settings.Fake.UrlDaApi.TrimEnd('/')}/api/v1/provedor-fake/checkout/{id}";

        return Task.FromResult(Result.Ok(new SessaoDeCheckout(id, url)));
    }

    /// <inheritdoc />
    /// <remarks>Como no PSP, sessão cancelada deixa de existir: a página e o "pagar" dela respondem 404.</remarks>
    public Task<Result> Cancelar(string idExterno, CancellationToken ct = default)
    {
        _sessoes.TryRemove(idExterno, out _);

        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Result<EventoDoProvedor> LerWebhook(string corpo, string? assinaturaHmac)
    {
        if (string.IsNullOrEmpty(Settings.SegredoDoWebhook) || string.IsNullOrWhiteSpace(assinaturaHmac))
            return AssinaturaInvalida;

        var esperado = Encoding.ASCII.GetBytes(Assinar(corpo, Settings.SegredoDoWebhook));
        var recebido = Encoding.ASCII.GetBytes(assinaturaHmac.Trim().ToLowerInvariant());

        if (!CryptographicOperations.FixedTimeEquals(esperado, recebido))
            return AssinaturaInvalida;

        try
        {
            return JsonSerializer.Deserialize<EventoDoProvedor>(corpo, Json) is { Id.Length: > 0, Tipo.Length: > 0 } evento
                ? evento
                : PayloadInvalido;
        }
        catch (JsonException)
        {
            return PayloadInvalido;
        }
    }

    /// <inheritdoc />
    public Task<Result<EventoDoProvedor?>> ConsultarPagamento(Guid assinaturaId, string? idExterno, CancellationToken ct = default) =>
        Task.FromResult(
            Result.Ok(_sessoes.Values.FirstOrDefault(sessao => sessao.Pedido.AssinaturaId == assinaturaId && sessao.Pagamento is not null)?.Pagamento)
        );

    /// <summary>Sessão de checkout aberta, para a página fake desenhar.</summary>
    /// <param name="id">Id da sessão.</param>
    public SessaoFake? ObterSessao(string id) => _sessoes.GetValueOrDefault(id);

    /// <summary>
    /// Simula a decisão do pagador na página do provedor e devolve o webhook que o PSP mandaria.
    /// </summary>
    /// <remarks>
    /// Pagamento aprovado fica registrado na sessão — é o que <see cref="ConsultarPagamento"/> devolve
    /// quando o webhook se perde.
    /// </remarks>
    /// <param name="id">Id da sessão.</param>
    /// <param name="aprovado">Pagou (<c>true</c>) ou o cartão foi recusado.</param>
    /// <returns>Corpo e assinatura do webhook, ou nulo se a sessão não existir.</returns>
    public WebhookFake? Pagar(string id, bool aprovado)
    {
        if (!_sessoes.TryGetValue(id, out var sessao))
            return null;

        var evento = new EventoDoProvedor(
            $"evt_{Guid.CreateVersion7():N}",
            aprovado ? TiposDeEvento.PagamentoConfirmado : TiposDeEvento.PagamentoRecusado,
            sessao.Pedido.AssinaturaId,
            id
        );

        if (aprovado)
            _sessoes[id] = sessao with { Pagamento = evento };

        var corpo = JsonSerializer.Serialize(evento, Json);

        return new WebhookFake(corpo, Assinar(corpo, Settings.SegredoDoWebhook));
    }

    /// <summary>HMAC-SHA256 do corpo, em hexadecimal minúsculo — o esquema de assinatura do fake.</summary>
    /// <param name="corpo">Corpo exato.</param>
    /// <param name="segredo">Segredo compartilhado.</param>
    public static string Assinar(string corpo, string segredo) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(corpo)));

    private static Result<EventoDoProvedor> AssinaturaInvalida =>
        Result.Falha<EventoDoProvedor>(Erro.NaoAutenticado("webhook.assinatura_invalida", "Assinatura do webhook inválida."));

    private static Result<EventoDoProvedor> PayloadInvalido =>
        Result.Falha<EventoDoProvedor>(Erro.Validacao("webhook.payload_invalido", "Corpo do webhook ilegível."));
}

/// <summary>Sessão de checkout do provedor fake.</summary>
/// <param name="Id">Id da sessão.</param>
/// <param name="Pedido">O que foi pedido no checkout.</param>
/// <param name="Pagamento">Evento de confirmação, depois que o pagador aprova.</param>
public sealed record SessaoFake(string Id, PedidoDeCheckout Pedido, EventoDoProvedor? Pagamento);

/// <summary>Webhook pronto para entregar.</summary>
/// <param name="Corpo">Corpo JSON.</param>
/// <param name="Assinatura">HMAC do corpo.</param>
public sealed record WebhookFake(string Corpo, string Assinatura);
