using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Cobrancas.Interfaces;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Cobrancas;

/// <summary>
/// Os opcionais da turma: o que o formando pode pedir só para ele.
/// </summary>
/// <remarks>
/// Todo membro lê a vitrine; cadastrar é da Tesouraria (P3) — item opcional é preço, e preço é
/// tesouraria. Não há rota de plano aqui: os opcionais moram no plano <b>vigente</b>, que é o mesmo
/// que a adesão lê.
/// <para>
/// O item opcional é um <c>ItemDeCobranca</c> marcado como sob demanda (decisão 1), e por isso
/// ele também aparece em <c>GET /cobrancas/planos/{id}</c>, com <c>opcional</c> verdadeiro — é
/// de lá que a cartão Opcionais da tela de Plano lê a lista da tesouraria, inclusive os encerrados.
/// </para>
/// </remarks>
/// <param name="opcionaisService">Regras dos opcionais.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Cobrancas)]
[Route("api/v{version:apiVersion}/cobrancas/opcionais")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class OpcionaisController(IOpcionaisService opcionaisService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Os itens que o formando pode pedir — a vitrine de "Minhas parcelas".</summary>
    /// <remarks>
    /// Traz também o que ainda não abriu, para o cartão mostrar a data no lugar do botão. O
    /// encerrado e o que passou do prazo ficam de fora: oferecer o que a API recusa é pior que não
    /// oferecer.
    /// </remarks>
    [HttpGet]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(IEnumerable<OpcionalDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Responder((await opcionaisService.Listar(ct)).Map(itens => itens.Select(item => item.Adapt<OpcionalDTO>())));

    /// <summary>Cadastra um item opcional no plano vigente.</summary>
    /// <param name="requisicao">Preço unitário, parcelas, cota, prazo, estoque, abertura e o item da festa.</param>
    [HttpPost]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("cobranca.opcional_criado")]
    [ProducesResponseType(typeof(ItemDeCobrancaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Criar([FromBody] OpcionalRequestDTO requisicao, CancellationToken ct) =>
        Responder((await opcionaisService.Criar(requisicao.ParaModelo(), ct)).Map(item => item.Adapt<ItemDeCobrancaDTO>()));

    /// <summary>Corrige um item opcional.</summary>
    /// <remarks>
    /// Com parcela gerada, só o preço e a descrição mudam — 409 <c>cobranca.item_em_uso</c> no resto.
    /// Estoque abaixo do já reservado, 409 <c>cobranca.estoque_menor_que_reservado</c> (P8).
    /// </remarks>
    /// <param name="id">Item opcional.</param>
    /// <param name="requisicao">Dados novos.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(ItemDeCobrancaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] OpcionalRequestDTO requisicao, CancellationToken ct) =>
        Responder((await opcionaisService.Atualizar(id, requisicao.ParaModelo(), usuarioAtual.Id, ct)).Map(item => item.Adapt<ItemDeCobrancaDTO>()));

    /// <summary>Encerra a venda: para de aceitar pedido, e o que já foi pedido fica.</summary>
    /// <param name="id">Item opcional.</param>
    [HttpPost("{id:guid}/encerrar")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(ItemDeCobrancaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Encerrar(Guid id, CancellationToken ct) =>
        Responder((await opcionaisService.Encerrar(id, usuarioAtual.Id, ct)).Map(item => item.Adapt<ItemDeCobrancaDTO>()));

    /// <summary>Exclui um item que nunca foi pedido. Com pedido, 409 <c>cobranca.item_com_pedido</c>: encerre-o.</summary>
    /// <param name="id">Item opcional.</param>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct) => Responder(await opcionaisService.Excluir(id, usuarioAtual.Id, ct));
}
