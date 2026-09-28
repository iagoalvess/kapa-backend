using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Comum;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Cobrancas;

/// <summary>
/// O que cada formando pediu só para ele: o convite a mais, o kit, a foto.
/// </summary>
/// <remarks>
/// Pedir é do formando, com adesão vigente e turma ativa (decisão 4); a lista da turma é da Gestão,
/// que é quem responde ao formando que diz "pedi e não apareceu". Cancelar é do próprio dono ou da
/// tesouraria — e é o cancelamento que devolve o estoque (decisão 9).
/// <para>
/// O pedido de outro responde 404, nunca 403: distinguir os dois transformaria a rota num
/// verificador de quem pediu o quê.
/// </para>
/// </remarks>
/// <param name="pedidoService">Regras do pedido.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Cobrancas)]
[Route("api/v{version:apiVersion}/pedidos")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class PedidoController(IPedidoService pedidoService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Os pedidos do próprio formando, do mais novo para o mais antigo.</summary>
    [HttpGet("meus")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(IEnumerable<PedidoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarMeus(CancellationToken ct) =>
        Responder(
            (await pedidoService.ListarMeus(FormaturaId, usuarioAtual.Id, ct)).Map(pedidos => pedidos.Select(pedido => pedido.Adapt<PedidoDTO>()))
        );

    /// <summary>Os pedidos da turma, do mais novo para o mais antigo.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="itemDeCobrancaId">Só os deste item opcional.</param>
    /// <param name="status"><c>Confirmado</c> ou <c>Cancelado</c>.</param>
    /// <param name="busca">Trecho do nome da conta ou do nome civil de quem pediu.</param>
    /// <param name="quitado">Só os pagos por inteiro, ou só os que ainda devem.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(PaginaDTO<PedidoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] Guid? itemDeCobrancaId,
        [FromQuery] StatusDoPedido? status,
        [FromQuery] string? busca,
        [FromQuery] bool? quitado,
        CancellationToken ct
    )
    {
        var resultado = await pedidoService.Listar(paginacao.ParaModelo(), new FiltroDePedidos(itemDeCobrancaId, status, busca, quitado), ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(pedido => pedido.Adapt<PedidoDTO>())));
    }

    /// <summary>A conta aberta de cada item opcional — a faixa do topo da tela de Pedidos.</summary>
    /// <remarks>
    /// "Restam 12" quer dizer <b>reservados</b>, não pagos. A faixa mostra as duas contas de
    /// propósito: sem baixa automática, a diferença entre pedido e dinheiro é o que a tesouraria
    /// precisa enxergar.
    /// </remarks>
    [HttpGet("resumo")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(IEnumerable<ResumoDoItemPedidoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resumir(CancellationToken ct) =>
        Responder((await pedidoService.Resumir(ct)).Map(itens => itens.Select(item => item.Adapt<ResumoDoItemPedidoDTO>())));

    /// <summary>
    /// Pede, reserva o estoque e grava as parcelas — na mesma transação.
    /// </summary>
    /// <remarks>
    /// Não há aprovação (P2): o pedido nasce confirmado e a tela cai no PIX da primeira parcela.
    /// Pedir de novo o mesmo item ajusta a quantidade do pedido que já existe — é um pedido por item
    /// por formando (decisão 3), e o clique duplo vira um pedido só pelo índice único.
    /// </remarks>
    /// <param name="requisicao">Item e quantidade absoluta.</param>
    [HttpPost]
    [FilaPorTurma]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("cobranca.pedido_feito")]
    [ProducesResponseType(typeof(PedidoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Pedir([FromBody] PedidoRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (
                await pedidoService.Pedir(
                    FormaturaId,
                    usuarioAtual.Id,
                    new DadosDoPedido(requisicao.ItemDeCobrancaId, requisicao.Quantidade, requisicao.Parcelas),
                    ct
                )
            ).Map(pedido => pedido.Adapt<PedidoDTO>())
        );

    /// <summary>Muda a quantidade de um pedido próprio. A quantidade é absoluta: repetir é um no-op.</summary>
    /// <param name="id">Pedido.</param>
    /// <param name="requisicao">Quantidade final.</param>
    [HttpPut("{id:guid}")]
    [FilaPorTurma]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(PedidoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Ajustar(Guid id, [FromBody] QuantidadeDoPedidoRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (await pedidoService.Ajustar(id, FormaturaId, usuarioAtual.Id, requisicao.Quantidade, ct)).Map(pedido => pedido.Adapt<PedidoDTO>())
        );

    /// <summary>
    /// Cancela um pedido: o estoque volta e as parcelas em aberto são canceladas.
    /// </summary>
    /// <remarks>
    /// O próprio dono cancela enquanto nada foi pago; depois disso é da tesouraria. Sem crédito e com
    /// parcela paga, o pedido encolhe para o <c>piso(pago ÷ preço unitário)</c> (P9). Com crédito, o
    /// pedido inteiro cai e o valor vira parcela negativa dele (P5).
    /// </remarks>
    /// <param name="id">Pedido.</param>
    /// <param name="requisicao">Crédito a devolver, se houver.</param>
    [HttpPost("{id:guid}/cancelar")]
    [FilaPorTurma]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(PedidoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancelar(Guid id, [FromBody] CancelarPedidoRequestDTO? requisicao, CancellationToken ct) =>
        Responder(
            (await pedidoService.Cancelar(id, FormaturaId, usuarioAtual.Id, new CancelamentoDePedido(requisicao?.CreditoEmCentavos ?? 0), ct)).Map(
                pedido => pedido.Adapt<PedidoDTO>()
            )
        );
}
