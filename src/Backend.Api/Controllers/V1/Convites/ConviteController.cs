using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Convites;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Convites.Interfaces;
using Backend.Business.Convites.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Convites;

/// <summary>
/// Convites da formatura selecionada, do lado da comissão: criar, acompanhar e revogar.
/// </summary>
/// <remarks>
/// Gestão cria e revoga convite de Formando; os demais papéis exigem o Presidente, e quem decide é
/// o service, que conhece o papel oferecido. Criar e revogar valem desde o rascunho, para a
/// comissão se montar antes de contratar; convite de Formando só depois do pagamento (service).
/// </remarks>
/// <param name="conviteService">Criação e revogação.</param>
/// <param name="usuarioAtual">Quem está fazendo a requisição.</param>
/// <param name="formaturaAtual">Formatura da sessão.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/formaturas/atual/convites")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ConviteController(IConviteService conviteService, IUsuarioAtual usuarioAtual, IFormaturaAtual formaturaAtual) : MainController
{
    /// <summary>A política garante a claim; o <c>Guid.Empty</c> nunca chega a ser consultado.</summary>
    private Guid FormaturaId => formaturaAtual.Id ?? Guid.Empty;

    /// <summary>
    /// Cria o convite — nominal, com e-mail, ou o link aberto da turma — e devolve o link uma única vez.
    /// </summary>
    /// <remarks>O banco guarda só o hash do token: não há como mostrar o link de novo depois.</remarks>
    /// <param name="requisicao">E-mail, papel, validade e limite de entradas.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaEditavel)]
    [RegistrarEvento("convite.criado")]
    [ProducesResponseType(typeof(ConviteCriadoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Criar([FromBody] CriarConviteRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await conviteService.Criar(FormaturaId, usuarioAtual.Id, requisicao.Adapt<CriarConvite>(), ct);

        return Responder(resultado.Map(criado => criado.Adapt<ConviteCriadoDTO>()));
    }

    /// <summary>Os convites mais recentes, com situação e quantas pessoas entraram por cada um.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(IReadOnlyList<ConviteResumoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(CancellationToken ct)
    {
        var resultado = await conviteService.Listar(ct);

        return Responder(resultado.Map(convites => convites.Adapt<List<ConviteResumoDTO>>()));
    }

    /// <summary>Revoga o convite: o link para de funcionar na hora. Quem já entrou continua.</summary>
    /// <param name="id">Convite.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaEditavel)]
    [RegistrarEvento("convite.revogado", CamposDaRota = ["id"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revogar(Guid id, CancellationToken ct)
    {
        var resultado = await conviteService.Revogar(FormaturaId, usuarioAtual.Id, id, ct);

        return Responder(resultado);
    }
}
