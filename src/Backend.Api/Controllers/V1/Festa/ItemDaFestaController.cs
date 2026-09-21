using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Festa;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Festa;

/// <summary>
/// O que a turma está comprando, e quanto falta para pagar por isso.
/// </summary>
/// <remarks>
/// Leitura para todo membro, escrita para a Gestão (decisão 5): a descrição é da comissão e o preço é
/// da tesouraria, e são o mesmo registro.
/// <para>
/// Item com despesa lançada não é excluído: 409 <c>festa.item_em_uso</c>, e o caminho é cancelar —
/// a mesma forma do <c>financeiro.fornecedor_em_uso</c> da Sprint 10.
/// </para>
/// </remarks>
/// <param name="itemService">Regras dos itens e da meta.</param>
/// <param name="usuarioAtual">Quem chama — é o que decide de quem é o voto em cada proposta.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Mural)]
[Route("api/v{version:apiVersion}/festa")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ItemDaFestaController(IItemDaFestaService itemService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Nome da rota do detalhe, para o <c>Location</c> da criação.</summary>
    public const string RotaDoItem = "ItemDaFestaPorId";

    /// <summary>Os itens da festa, na ordem da comissão.</summary>
    /// <remarks>Turma que nunca abriu a tela recebe aqui os seis itens sugeridos (decisão 12).</remarks>
    [HttpGet("itens")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(IEnumerable<ItemDaFestaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Responder((await itemService.Listar(ct)).Map(itens => itens.Select(item => item.Adapt<ItemDaFestaDTO>())));

    /// <summary>A meta da turma: custo da festa, arrecadado e o que falta juntar.</summary>
    [HttpGet("meta")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(MetaDaFestaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterMeta(CancellationToken ct) =>
        Responder((await itemService.ObterMeta(ct)).Map(meta => meta.Adapt<MetaDaFestaDTO>()));

    /// <summary>Um item da festa.</summary>
    /// <param name="id">Item.</param>
    [HttpGet("itens/{id:guid}", Name = RotaDoItem)]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(ItemDaFestaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct) =>
        Responder((await itemService.ObterPorId(id, ct)).Map(item => item.Adapt<ItemDaFestaDTO>()));

    /// <summary>Um item com as propostas levantadas para ele — o painel da direita da tela.</summary>
    /// <remarks>
    /// As propostas não vêm na listagem: a lista da esquerda só precisa da contagem, e é este
    /// endpoint que sabe de quem é o voto, porque é o único que recebe quem está lendo.
    /// </remarks>
    /// <param name="id">Item.</param>
    [HttpGet("itens/{id:guid}/detalhe")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(ItemDaFestaDetalheDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterDetalhe(Guid id, CancellationToken ct) =>
        Responder(
            (await itemService.ObterDetalhe(id, FormaturaId, usuarioAtual.Id, ct)).Map(detalhe => new ItemDaFestaDetalheDTO(
                detalhe.Item.Adapt<ItemDaFestaDTO>(),
                detalhe.Propostas.Select(proposta => proposta.Adapt<PropostaDTO>())
            ))
        );

    /// <summary>Cria um item no fim da lista.</summary>
    /// <param name="requisicao">Título, categoria, o que inclui, rateio e valor.</param>
    [HttpPost("itens")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.item_criado")]
    [ProducesResponseType(typeof(ItemDaFestaDTO), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Criar([FromBody] ItemDaFestaRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await itemService.Criar(ParaModelo(requisicao), ct);

        return Criado(
            resultado.Map(item => item.Adapt<ItemDaFestaDTO>()),
            RotaDoItem,
            new { id = resultado.Sucesso ? resultado.Valor.Id : Guid.Empty }
        );
    }

    /// <summary>Corrige um item.</summary>
    /// <param name="id">Item.</param>
    /// <param name="requisicao">Dados novos.</param>
    [HttpPut("itens/{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.item_alterado", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(ItemDaFestaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] ItemDaFestaRequestDTO requisicao, CancellationToken ct) =>
        Responder((await itemService.Atualizar(id, ParaModelo(requisicao), ct)).Map(item => item.Adapt<ItemDaFestaDTO>()));

    /// <summary>A turma desistiu: o item sai do custo da festa e fica na lista com o selo.</summary>
    /// <remarks>As despesas dele não são canceladas (decisão 13): o que já saiu do caixa continua no balancete.</remarks>
    /// <param name="id">Item.</param>
    [HttpPost("itens/{id:guid}/cancelamento")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.item_cancelado", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(ItemDaFestaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancelar(Guid id, CancellationToken ct) =>
        Responder((await itemService.Cancelar(id, ct)).Map(item => item.Adapt<ItemDaFestaDTO>()));

    /// <summary>Desfaz o cancelamento — o item volta a contar no custo da festa.</summary>
    /// <param name="id">Item.</param>
    [HttpDelete("itens/{id:guid}/cancelamento")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.item_reativado", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(ItemDaFestaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reativar(Guid id, CancellationToken ct) =>
        Responder((await itemService.Reativar(id, ct)).Map(item => item.Adapt<ItemDaFestaDTO>()));

    /// <summary>Exclui um item que nunca teve despesa.</summary>
    /// <remarks>Com despesa, 409 <c>festa.item_em_uso</c>: cancele em vez de excluir.</remarks>
    /// <param name="id">Item.</param>
    [HttpDelete("itens/{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.item_excluido", CamposDaRota = ["id"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct) => Responder(await itemService.Excluir(id, ct));

    /// <summary>O corpo como o service o espera; rateio ausente é da turma, e quantidade ausente é 1.</summary>
    private static DadosDoItemDaFesta ParaModelo(ItemDaFestaRequestDTO requisicao) =>
        new(
            requisicao.Titulo ?? string.Empty,
            requisicao.Categoria,
            requisicao.OQueInclui,
            requisicao.DocumentoId,
            requisicao.Rateio ?? TipoDeRateio.Turma,
            requisicao.ValorPrevistoEmCentavos,
            requisicao.QuantidadeEstimada ?? 1
        );
}
