using System.Globalization;
using System.Net;
using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Data.Provedores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Assinaturas;

/// <summary>
/// A "página do provedor" do <see cref="ProvedorFake"/>: onde o checkout hospedado cai em
/// desenvolvimento e na demonstração.
/// </summary>
/// <remarks>
/// Não é endpoint do produto — faz o papel do PSP. Por isso tem decisão aqui dentro, e por isso
/// responde 404 fora de <c>Development</c>/<c>Testing</c> e com qualquer outro provedor: numa API
/// real, "pagar" sem pagar é o buraco que o webhook assinado existe para fechar. Lista de
/// permissão, não de bloqueio: um staging chamado <c>Homologacao</c> não pode virar "ative qualquer
/// turma de graça".
/// <para>
/// Ao pagar, entrega o webhook assinado pelo mesmo <see cref="IWebhookService.Receber"/> que o
/// endpoint público usa — com HMAC e tudo —, a menos que <c>Assinaturas:Fake:EntregarWebhook</c>
/// esteja desligado para simular o webhook perdido. Depois devolve o navegador ao front, como o PSP.
/// </para>
/// </remarks>
/// <param name="provedor">Provedor ativo — só serve se for o fake.</param>
/// <param name="webhookService">Recebedor do webhook.</param>
/// <param name="ambiente">Ambiente de execução.</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/provedor-fake/checkout")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ProvedorFakeController(IProvedorDeAssinatura provedor, IWebhookService webhookService, IHostEnvironment ambiente) : ControllerBase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private ProvedorFake? Fake => ambiente.IsDevelopment() || ambiente.IsEnvironment("Testing") ? provedor as ProvedorFake : null;

    /// <summary>Página de pagamento simulada.</summary>
    /// <param name="id">Sessão de checkout.</param>
    [HttpGet("{id}")]
    public IActionResult Pagina(string id)
    {
        if (Fake is not { } fake || fake.ObterSessao(id) is not { } sessao)
            return NotFound();

        var pedido = sessao.Pedido;
        var preco = (pedido.PrecoEmCentavos / 100m).ToString("C", PtBr);
        var entrega = fake.EntregaWebhook ? "O webhook será entregue ao pagar." : "Webhook desligado: só a conciliação vai achar este pagamento.";

        return Content(
            $"""
            <!doctype html>
            <html lang="pt-BR">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Provedor fake — pagamento</title></head>
            <body style="font-family:system-ui,sans-serif;background:#f4f4f5;display:grid;place-items:center;min-height:100vh;margin:0">
              <main style="background:#fff;border-radius:16px;padding:32px;max-width:380px;width:100%;box-shadow:0 1px 3px #0002">
                <p style="margin:0;color:#b45309;font-size:12px;font-weight:600;text-transform:uppercase">Ambiente de simulação — nada é cobrado</p>
                <h1 style="font-size:20px;margin:12px 0 4px">Plano {WebUtility.HtmlEncode(pedido.PlanoNome)}</h1>
                <p style="font-size:28px;font-weight:700;margin:0">{preco} <span style="font-size:14px;font-weight:400;color:#666">/ {pedido.Ciclo.ToString().ToLowerInvariant()}</span></p>
                <p style="color:#666;font-size:13px">{entrega}</p>
                <form method="post" action="{id}/pagar"><button style="width:100%;padding:12px;border:0;border-radius:8px;background:#1c1c1a;color:#fff;font-size:15px;cursor:pointer">Pagar</button></form>
                <form method="post" action="{id}/recusar" style="margin-top:8px"><button style="width:100%;padding:12px;border:1px solid #ddd;border-radius:8px;background:#fff;font-size:15px;cursor:pointer">Recusar pagamento</button></form>
              </main>
            </body>
            </html>
            """,
            "text/html; charset=utf-8"
        );
    }

    /// <summary>Aprova o pagamento, entrega o webhook e devolve o navegador ao front.</summary>
    /// <param name="id">Sessão de checkout.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("{id}/pagar")]
    public Task<IActionResult> Pagar(string id, CancellationToken ct) => Decidir(id, aprovado: true, ct);

    /// <summary>Recusa o pagamento, entrega o webhook e devolve o navegador ao front.</summary>
    /// <param name="id">Sessão de checkout.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("{id}/recusar")]
    public Task<IActionResult> Recusar(string id, CancellationToken ct) => Decidir(id, aprovado: false, ct);

    private async Task<IActionResult> Decidir(string id, bool aprovado, CancellationToken ct)
    {
        if (Fake is not { } fake || fake.ObterSessao(id) is not { } sessao || fake.Pagar(id, aprovado) is not { } webhook)
            return NotFound();

        if (fake.EntregaWebhook)
            await webhookService.Receber(webhook.Corpo, webhook.Assinatura, ct);

        return Redirect(sessao.Pedido.UrlDeRetorno);
    }
}
