using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Festa;
using Backend.Api.DTOs.Loja;
using Backend.Business.Abstractions;
using Backend.Business.Festa.Models;
using Backend.Business.Loja.Interfaces;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Loja;

/// <summary>
/// A loja pública da turma — sem conta, sem sessão (Sprint 26).
/// </summary>
/// <remarks>
/// A vitrine e a compra são da turma da rota: o link é aberto (P1), e a loja só mostra o que a turma pôs à
/// venda. A compra depois de criada é do link assinado (decisão 10): quem tem o link a abre, nomeia e apaga.
/// Todas as rotas são anônimas e limitadas por IP, com rajada (<see cref="RateLimitConfig.Loja"/> e
/// <see cref="RateLimitConfig.Vitrine"/>).
/// </remarks>
/// <param name="loja">Regras da loja.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/loja")]
[AllowAnonymous]
public sealed class LojaController(ILojaService loja) : MainController
{
    /// <summary>A vitrine: os convites à venda, quantos restam e o relógio do servidor.</summary>
    /// <remarks>
    /// Vale dois segundos em cache (decisão 8): mil pessoas olhando o contador viram poucas leituras reais,
    /// e a borda segura o resto. 404 <c>loja.nao_encontrada</c> quando a turma não vende nada pela loja.
    /// </remarks>
    /// <param name="formaturaId">A turma.</param>
    [HttpGet("{formaturaId:guid}")]
    [EnableRateLimiting(RateLimitConfig.Vitrine)]
    [ResponseCache(Duration = 2, Location = ResponseCacheLocation.Any)]
    [ProducesResponseType(typeof(LojaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Abrir(Guid formaturaId, CancellationToken ct) =>
        Responder((await loja.AbrirLoja(formaturaId, ct)).Map(vitrine => vitrine.Adapt<LojaDTO>()));

    /// <summary>Compra: reserva na hora e devolve o link da compra com o documento para pagar.</summary>
    /// <remarks>
    /// Esgotado responde 409 <c>loja.esgotado</c> <b>antes</b> da fila da turma, numa leitura sem trava
    /// (decisão 8). Quem ainda tem chance entra na fila; fila cheia é 429 <c>loja.fila_cheia</c> com
    /// <c>Retry-After</c>, e a nova tentativa vai com a mesma <c>chave_de_idempotencia</c>. Limite por CPF é
    /// 409 <c>loja.limite_por_pessoa</c>; antes da abertura, 409 <c>cobranca.venda_nao_aberta</c>.
    /// </remarks>
    /// <param name="formaturaId">A turma.</param>
    /// <param name="requisicao">Comprador, quantidade e meio.</param>
    [HttpPost("{formaturaId:guid}/compras")]
    [EnableRateLimiting(RateLimitConfig.Loja)]
    [ProducesResponseType(typeof(CompraCriadaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Comprar(Guid formaturaId, [FromBody] CompraRequestDTO requisicao, CancellationToken ct) =>
        Responder((await loja.Comprar(formaturaId, requisicao.ParaModelo(), ct)).Map(criada => criada.Adapt<CompraCriadaDTO>()));

    /// <summary>Reenvia o link das compras deste e-mail — e o anterior deixa de abrir.</summary>
    /// <remarks>204 exista compra ou não: a rota não diz se alguém comprou (decisão 10).</remarks>
    /// <param name="formaturaId">A turma.</param>
    /// <param name="requisicao">E-mail da compra.</param>
    [HttpPost("{formaturaId:guid}/reenvio")]
    [EnableRateLimiting(RateLimitConfig.Autenticacao)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ReenviarLink(Guid formaturaId, [FromBody] ReenvioDoLinkRequestDTO requisicao, CancellationToken ct) =>
        Responder(await loja.ReenviarLink(formaturaId, requisicao.Email ?? string.Empty, ct));

    /// <summary>A compra pelo link: situação, documento para pagar e convites.</summary>
    /// <remarks>Link errado, antigo ou de compra inexistente: o mesmo 404 <c>loja.compra_nao_encontrada</c>.</remarks>
    /// <param name="token">O segredo do link.</param>
    [HttpGet("compras/{token}")]
    [EnableRateLimiting(RateLimitConfig.Vitrine)]
    [ProducesResponseType(typeof(CompraDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AbrirCompra(string token, CancellationToken ct) =>
        Responder((await loja.AbrirCompra(token, ct)).Map(compra => compra.Adapt<CompraDTO>()));

    /// <summary>Gera de novo o PIX da compra que ficou sem documento. Idempotente.</summary>
    /// <param name="token">O segredo do link.</param>
    [HttpPost("compras/{token}/cobranca")]
    [EnableRateLimiting(RateLimitConfig.Loja)]
    [ProducesResponseType(typeof(CompraDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> EmitirCobranca(string token, CancellationToken ct) =>
        Responder((await loja.EmitirCobranca(token, ct)).Map(compra => compra.Adapt<CompraDTO>()));

    /// <summary>Nomeia ou transfere um convite da compra.</summary>
    /// <remarks>As regras do convite da Sprint 21: até 24 h antes da festa, e trocar o titular troca o código.</remarks>
    /// <param name="token">O segredo do link.</param>
    /// <param name="id">Convite.</param>
    /// <param name="requisicao">Nome, documento e e-mail do convidado.</param>
    [HttpPut("compras/{token}/convites/{id:guid}/convidado")]
    [EnableRateLimiting(RateLimitConfig.Loja)]
    [ProducesResponseType(typeof(MeuConviteDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> NomearConvidado(string token, Guid id, [FromBody] ConvidadoRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (
                await loja.NomearConvidado(
                    token,
                    id,
                    new DadosDoConvidado(requisicao.Nome ?? string.Empty, requisicao.TipoDoDocumento, requisicao.NumeroDoDocumento, requisicao.Email),
                    ct
                )
            ).Map(convite => convite.Adapt<MeuConviteDTO>())
        );

    /// <summary>Apaga nome, e-mail e CPF de quem comprou — depois da festa, ou da compra que expirou.</summary>
    /// <remarks>Antes disso, 409 <c>loja.dados_ainda_necessarios</c>. Depois, o link deixa de abrir.</remarks>
    /// <param name="token">O segredo do link.</param>
    [HttpPost("compras/{token}/exclusao")]
    [EnableRateLimiting(RateLimitConfig.Loja)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApagarDados(string token, CancellationToken ct) => Responder(await loja.ApagarDados(token, ct));
}
