using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Business.Common;
using Backend.Business.Recebimentos.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Backend.Api.Controllers.V1.Recebimentos;

/// <summary>
/// O retorno da autorização da turma no Mercado Pago (OAuth) — a porta por onde o navegador do presidente volta
/// ao Kapa depois de autorizar, sem sessão (Sprint 25).
/// </summary>
/// <remarks>
/// Anônima por natureza, e não confia no que chega: o retorno só vale com o <c>state</c> assinado pelo Kapa no
/// clique do presidente. Fora do <c>[ExigeModulo]</c> do controller do provedor, que exige sessão.
/// </remarks>
/// <param name="provedorService">A conexão da turma.</param>
/// <param name="aplicacao">Para onde o navegador volta no front.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[EnableRateLimiting(RateLimitConfig.Webhook)]
public sealed class RetornoDoMercadoPagoController(IProvedorDaTurmaService provedorService, IOptions<AplicacaoSettings> aplicacao) : MainController
{
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
