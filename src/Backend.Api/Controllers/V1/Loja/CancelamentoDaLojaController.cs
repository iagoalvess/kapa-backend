using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Loja;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Loja.Interfaces;
using Backend.Business.Loja.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Loja;

/// <summary>
/// Desfazer compras da loja, do lado da turma (Sprint 38): cancelar, a devolução e a fila de pedidos do
/// comprador.
/// </summary>
/// <remarks>
/// Cancelar é da Gestão; marcar devolvida é da Tesouraria, que fez o PIX. As escritas que mexem no estoque do
/// item entram na fila da turma (decisão 1), a mesma da abertura de vendas, e exigem a turma ativa.
/// </remarks>
/// <param name="cancelamento">Regras do cancelamento.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Festa)]
[Route("api/v{version:apiVersion}/loja")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class CancelamentoDaLojaController(ICancelamentoDaCompraService cancelamento, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Os convites de uma compra, válidos e cancelados — para escolher o que cancelar.</summary>
    /// <param name="id">Compra.</param>
    [HttpGet("compras/{id:guid}/convites")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(IEnumerable<ConviteDaCompraDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListarConvites(Guid id, CancellationToken ct) =>
        Responder((await cancelamento.ListarConvites(id, ct)).Map(convites => convites.Adapt<IEnumerable<ConviteDaCompraDTO>>()));

    /// <summary>Cancela convites de uma compra paga — alguns ou todos —, com motivo.</summary>
    /// <remarks>
    /// Revoga, devolve o lugar, estorna a receita e põe a compra na lista a devolver, numa transação. Já cancelado:
    /// 409 <c>loja.compra_ja_cancelada</c>; com entrada na portaria: 409 <c>loja.convite_ja_validado</c>; não paga:
    /// 409 <c>loja.compra_nao_paga</c>.
    /// </remarks>
    /// <param name="id">Compra.</param>
    /// <param name="requisicao">Convites (vazio é todos) e motivo.</param>
    [HttpPost("compras/{id:guid}/cancelamento")]
    [FilaPorTurma]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(CompraCanceladaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancelar(Guid id, [FromBody] CancelamentoRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (
                await cancelamento.Cancelar(
                    id,
                    new DadosDoCancelamento(requisicao.ConviteIds, requisicao.Motivo ?? string.Empty),
                    usuarioAtual.Id,
                    ct
                )
            ).Map(cancelada => cancelada.Adapt<CompraCanceladaDTO>())
        );

    /// <summary>Marca a compra devolvida, com o comprovante do PIX de volta.</summary>
    /// <remarks>Multipart. Sem comprovante, 400 <c>loja.comprovante_obrigatorio</c>; fora da lista, 409 <c>loja.compra_nao_a_devolver</c>.</remarks>
    /// <param name="id">Compra.</param>
    /// <param name="comprovante">PDF ou imagem; obrigatório.</param>
    [HttpPost("compras/{id:guid}/devolucao")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarcarDevolvida(Guid id, IFormFile? comprovante, CancellationToken ct)
    {
        await using var conteudo = comprovante?.OpenReadStream() ?? Stream.Null;

        return Responder(await cancelamento.MarcarDevolvida(id, comprovante.ParaNovoArquivo(conteudo), usuarioAtual.Id, ct));
    }

    /// <summary>Os pedidos de cancelamento abertos, do mais antigo.</summary>
    [HttpGet("pedidos-de-cancelamento")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(IEnumerable<PedidoNaGestaoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarPedidos(CancellationToken ct) =>
        Responder((await cancelamento.ListarPedidos(ct)).Map(pedidos => pedidos.Adapt<IEnumerable<PedidoNaGestaoDTO>>()));

    /// <summary>Aprova o pedido: os convites pedidos são cancelados, como no cancelamento da Gestão.</summary>
    /// <param name="id">Pedido.</param>
    [HttpPost("pedidos-de-cancelamento/{id:guid}/aprovacao")]
    [FilaPorTurma]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(CompraCanceladaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Aprovar(Guid id, CancellationToken ct) =>
        Responder((await cancelamento.Aprovar(id, usuarioAtual.Id, ct)).Map(cancelada => cancelada.Adapt<CompraCanceladaDTO>()));

    /// <summary>Recusa o pedido, com motivo: os convites continuam valendo, e o comprador é avisado.</summary>
    /// <param name="id">Pedido.</param>
    /// <param name="requisicao">Motivo.</param>
    [HttpPost("pedidos-de-cancelamento/{id:guid}/recusa")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Recusar(Guid id, [FromBody] MotivoRequestDTO requisicao, CancellationToken ct) =>
        Responder(await cancelamento.Recusar(id, requisicao.Motivo ?? string.Empty, usuarioAtual.Id, ct));
}
