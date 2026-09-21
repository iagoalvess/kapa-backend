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
/// As candidatas de um item "a contratar", e o voto da turma nelas.
/// </summary>
/// <remarks>
/// A comissão levanta as propostas (decisão 16); qualquer membro vota, e cada um tem um voto por
/// item (decisão 17). A leitura mora no detalhe do item — aqui só existem as escritas.
/// <para>
/// Tudo isto é janela: depois de lançada a despesa do item, a escolha aconteceu, e escrever ou
/// votar devolve 409 <c>festa.disputa_encerrada</c>.
/// </para>
/// </remarks>
/// <param name="propostaService">Regras das propostas e dos votos.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Mural)]
[Route("api/v{version:apiVersion}/festa")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class PropostaController(IPropostaService propostaService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Acrescenta uma candidata ao item.</summary>
    /// <param name="id">Item, que precisa estar "a contratar".</param>
    /// <param name="requisicao">Título, valor e o que inclui.</param>
    [HttpPost("itens/{id:guid}/propostas")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.proposta_criada", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(PropostaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Criar(Guid id, [FromBody] PropostaRequestDTO requisicao, CancellationToken ct) =>
        Responder((await propostaService.Criar(id, ParaModelo(requisicao), ct)).Map(proposta => proposta.Adapt<PropostaDTO>()));

    /// <summary>Corrige uma proposta.</summary>
    /// <param name="id">Proposta.</param>
    /// <param name="requisicao">Dados novos.</param>
    [HttpPut("propostas/{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.proposta_alterada", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(PropostaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] PropostaRequestDTO requisicao, CancellationToken ct) =>
        Responder((await propostaService.Atualizar(id, ParaModelo(requisicao), ct)).Map(proposta => proposta.Adapt<PropostaDTO>()));

    /// <summary>Tira uma proposta da disputa — os votos nela vão junto.</summary>
    /// <param name="id">Proposta.</param>
    [HttpDelete("propostas/{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.proposta_excluida", CamposDaRota = ["id"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct) => Responder(await propostaService.Excluir(id, ct));

    /// <summary>O formando escolhe esta proposta, ou troca a que já tinha escolhido.</summary>
    /// <remarks>Repetir o voto na mesma proposta é o mesmo estado, e responde 204 de novo.</remarks>
    /// <param name="id">Proposta.</param>
    [HttpPut("propostas/{id:guid}/voto")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.voto_registrado", CamposDaRota = ["id"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Votar(Guid id, CancellationToken ct) =>
        Responder(await propostaService.Votar(id, FormaturaId, usuarioAtual.Id, ct));

    /// <summary>Tira o voto do formando naquele item.</summary>
    /// <param name="id">Item.</param>
    [HttpDelete("itens/{id:guid}/voto")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.voto_retirado", CamposDaRota = ["id"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Desvotar(Guid id, CancellationToken ct) =>
        Responder(await propostaService.Desvotar(id, FormaturaId, usuarioAtual.Id, ct));

    /// <summary>O corpo como o service o espera.</summary>
    private static DadosDaProposta ParaModelo(PropostaRequestDTO requisicao) =>
        new(requisicao.Titulo ?? string.Empty, requisicao.ValorEmCentavos, requisicao.OQueInclui);
}
