using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Loja.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Cobrancas;

/// <summary>
/// O cancelamento de pacote e de pedido como solicitação à comissão (Sprint 48, D8).
/// </summary>
/// <remarks>
/// O formando pede; a tesouraria aprova ou recusa — é ela quem mexe no dinheiro (D9). A Gestão inteira lê a fila.
/// Enquanto a solicitação espera, as parcelas do item saem da régua e da inadimplência, por até 7 dias (D12/D37).
/// </remarks>
/// <param name="solicitacoes">Regras da solicitação.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Cobrancas)]
[Route("api/v{version:apiVersion}/cobrancas/solicitacoes-de-cancelamento")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class SolicitacaoDeCancelamentoController(ISolicitacaoDeCancelamentoService solicitacoes, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Pede à comissão o cancelamento de um pacote da cesta ou de um pedido avulso.</summary>
    /// <remarks>
    /// Item que não é do formando, 404 <c>cobranca.nada_a_cancelar</c>; passado o "cancelável até", 409
    /// <c>cobranca.cancelamento_fora_do_prazo</c>. Pedir de novo com uma aberta devolve a mesma.
    /// </remarks>
    /// <param name="requisicao">O item e o motivo.</param>
    [HttpPost]
    [FilaPorTurma]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(SolicitacaoDeCancelamentoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Solicitar([FromBody] SolicitacaoDeCancelamentoRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (
                await solicitacoes.Solicitar(FormaturaId, usuarioAtual.Id, new DadosDaSolicitacao(requisicao.ItemDeCobrancaId, requisicao.Motivo), ct)
            ).Map(solicitacao => solicitacao.Adapt<SolicitacaoDeCancelamentoDTO>())
        );

    /// <summary>As solicitações do próprio formando, da mais nova para a mais antiga.</summary>
    [HttpGet("minhas")]
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [ProducesResponseType(typeof(IEnumerable<SolicitacaoDeCancelamentoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarMinhas(CancellationToken ct) =>
        Responder(
            (await solicitacoes.ListarMinhas(FormaturaId, usuarioAtual.Id, ct)).Map(lista =>
                lista.Select(solicitacao => solicitacao.Adapt<SolicitacaoDeCancelamentoDTO>())
            )
        );

    /// <summary>As solicitações da turma — a fila da comissão.</summary>
    /// <param name="status"><c>Aberto</c>, <c>Aprovado</c> ou <c>Recusado</c>; ausente, todas.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(IEnumerable<SolicitacaoDeCancelamentoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar([FromQuery] StatusDoPedidoDeCancelamento? status, CancellationToken ct) =>
        Responder(
            (await solicitacoes.Listar(status, ct)).Map(lista => lista.Select(solicitacao => solicitacao.Adapt<SolicitacaoDeCancelamentoDTO>()))
        );

    /// <summary>Aprova: cancela o pacote ou o pedido, e o já pago vai para a lista "a devolver" (D9).</summary>
    /// <param name="id">Solicitação.</param>
    [HttpPost("{id:guid}/aprovar")]
    [FilaPorTurma]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(SolicitacaoDeCancelamentoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Aprovar(Guid id, CancellationToken ct) =>
        Responder(
            (await solicitacoes.Aprovar(FormaturaId, id, usuarioAtual.Id, ct)).Map(solicitacao => solicitacao.Adapt<SolicitacaoDeCancelamentoDTO>())
        );

    /// <summary>Recusa, com motivo: a cobrança volta no mesmo dia.</summary>
    /// <param name="id">Solicitação.</param>
    /// <param name="requisicao">O motivo, que o formando lê.</param>
    [HttpPost("{id:guid}/recusar")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(SolicitacaoDeCancelamentoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Recusar(Guid id, [FromBody] RecusaDaSolicitacaoRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (await solicitacoes.Recusar(id, requisicao.Motivo ?? string.Empty, usuarioAtual.Id, ct)).Map(solicitacao =>
                solicitacao.Adapt<SolicitacaoDeCancelamentoDTO>()
            )
        );
}
