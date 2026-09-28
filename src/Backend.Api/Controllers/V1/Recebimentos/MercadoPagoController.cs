using System.Text;
using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Business.Common;
using Backend.Business.Pagamentos.Services;
using Backend.Business.Recebimentos.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Backend.Api.Controllers.V1.Recebimentos;

/// <summary>
/// As duas portas por onde o Mercado Pago fala com o Kapa sem sessão: o aviso de pagamento (webhook) e o
/// retorno da autorização da turma (OAuth) — Sprint 25.
/// </summary>
/// <remarks>
/// Anônimas por natureza, e nenhuma confia no que chega: o aviso só é aceito com a assinatura conferida e
/// o valor pago é perguntado ao Mercado Pago (decisão 12); o retorno só vale com o <c>state</c> assinado
/// pelo Kapa no clique do presidente. Fora do <c>[ExigeModulo]</c> do controller da conta, que exige sessão.
/// </remarks>
/// <param name="aviso">O processamento do aviso.</param>
/// <param name="provedorService">A conexão da turma.</param>
/// <param name="aplicacao">Para onde o navegador volta no front.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[EnableRateLimiting(RateLimitConfig.Webhook)]
public sealed class MercadoPagoController(AvisoDoMercadoPago aviso, IProvedorDaTurmaService provedorService, IOptions<AplicacaoSettings> aplicacao)
    : MainController
{
    /// <summary>O aviso do Mercado Pago — o recebedor único, das turmas e da conta do Kapa (Sprint 35).</summary>
    /// <remarks>
    /// 200 quando o aviso foi entendido — inclusive o de pedido que não é do Kapa, senão ele seria
    /// reentregue para sempre. 401 com assinatura inválida; 503 quando a consulta ao Mercado Pago falhou, e
    /// ele reentrega. Do corpo só se lê o <c>user_id</c>, que decide se o aviso é da conta do Kapa.
    /// </remarks>
    /// <param name="idDoRecurso">O id do recurso (<c>data.id</c>).</param>
    /// <param name="tipo">O tópico (<c>type</c>).</param>
    [HttpPost("webhooks/cobranca/mercadopago")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Aviso(
        [FromQuery(Name = "data.id")] string? idDoRecurso,
        [FromQuery(Name = "type")] string? tipo,
        CancellationToken ct
    )
    {
        using var leitor = new StreamReader(Request.Body, Encoding.UTF8);
        var corpo = await leitor.ReadToEndAsync(ct);

        var resultado = await aviso.Receber(
            Request.Headers["x-signature"].ToString(),
            Request.Headers["x-request-id"].ToString(),
            idDoRecurso,
            tipo,
            corpo,
            ct
        );

        return resultado.Sucesso ? Ok() : Responder(resultado);
    }

    /// <summary>O retorno da autorização: grava a conexão e manda o navegador de volta para a tela da turma.</summary>
    /// <remarks>
    /// Sempre redireciona — é o navegador do presidente, não um cliente de API. O front lê
    /// <c>mercado_pago=conectado</c> ou <c>mercado_pago=erro</c> com o <c>codigo</c> do erro e avisa. O presidente
    /// que negou a autorização volta com <c>error</c> e sem <c>code</c>, e cai no mesmo aviso de erro.
    /// </remarks>
    /// <param name="codigo">O <c>code</c> do OAuth.</param>
    /// <param name="state">O <c>state</c> assinado no clique.</param>
    [HttpGet("mercado-pago/retorno")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> Retorno([FromQuery(Name = "code")] string? codigo, [FromQuery] string? state, CancellationToken ct)
    {
        var resultado = await provedorService.ConcluirConexao(codigo, state, ct);

        var parametros = resultado.Sucesso
            ? new Dictionary<string, string> { ["mercado_pago"] = "conectado" }
            : new Dictionary<string, string> { ["mercado_pago"] = "erro", ["codigo"] = resultado.PrimeiroErro.Codigo };

        return Redirect(aplicacao.Value.MontarUrl(RotasDoFront.Formatura, parametros));
    }
}
