using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Pagamentos;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Pagamentos.Interfaces;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Pagamentos;

/// <summary>
/// O que o formando deve: em aberto, a próxima a pagar e todas as parcelas.
/// </summary>
/// <remarks>
/// Só o próprio — não existe "extrato de fulano" aqui. Quem precisa olhar o de outra pessoa passa
/// pela listagem de parcelas da tesouraria, que é onde a permissão para isso existe.
/// </remarks>
/// <param name="pagamentoService">Regras do pagamento.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Pix)]
[Route("api/v{version:apiVersion}/extrato")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ExtratoController(IPagamentoService pagamentoService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>O extrato do próprio formando: em aberto, a próxima a pagar e todas as parcelas.</summary>
    /// <remarks>Aceita o desligado: o extrato é a prova do que ele pagou, e não some com a saída (P5).</remarks>
    [HttpGet("eu")]
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [ProducesResponseType(typeof(ExtratoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterExtrato(CancellationToken ct) =>
        Responder((await pagamentoService.ObterExtrato(FormaturaId, usuarioAtual.Id, ct)).Map(extrato => extrato.Adapt<ExtratoDTO>()));

    /// <summary>Quantas parcelas próprias venceram sem aviso de pagamento — o selo do menu.</summary>
    /// <remarks>
    /// Existe para não pedir o extrato inteiro em toda tela do app: o selo é um número, e o extrato
    /// de quem está no fim da turma passa de dezenas de parcelas.
    /// </remarks>
    [HttpGet("eu/pendencias")]
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [ProducesResponseType(typeof(PendenciasDoExtratoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterPendencias(CancellationToken ct) =>
        Responder(
            (await pagamentoService.ContarVencidasSemAviso(FormaturaId, usuarioAtual.Id, ct)).Map(vencidas => new PendenciasDoExtratoDTO(vencidas))
        );
}
