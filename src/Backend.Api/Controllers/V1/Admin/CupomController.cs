using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Assinaturas;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Services;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Admin;

/// <summary>
/// Cupons de desconto da primeira cobrança, no painel do Kapa (Sprint 51).
/// </summary>
/// <remarks>
/// <see cref="Politicas.SomenteAdministrador"/> na classe, como o <c>AdminController</c>. Criar e desativar gravam
/// evento de auditoria com o autor. Não há editar: cupom usado com outro percentual faria o histórico mentir.
/// </remarks>
/// <param name="cupomService">Cupons.</param>
/// <param name="usuarioAtual">Administrador que executa — o autor dos eventos.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/cupons")]
[Authorize(Policy = Politicas.SomenteAdministrador)]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class CupomController(CupomService cupomService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Todos os cupons, os mais novos primeiro.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CupomDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Responder((await cupomService.Listar(ct)).Map(cupons => cupons.Adapt<IReadOnlyList<CupomDTO>>()));

    /// <summary>Cria um cupom. Percentual de 1 a 50; validade e limite de usos obrigatórios.</summary>
    /// <param name="requisicao">Cupom novo.</param>
    [HttpPost]
    [ProducesResponseType(typeof(CupomDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Criar([FromBody] NovoCupomRequestDTO requisicao, CancellationToken ct) =>
        Responder((await cupomService.Criar(requisicao.Adapt<NovoCupom>(), usuarioAtual.Id, ct)).Map(cupom => cupom.Adapt<CupomDTO>()));

    /// <summary>Desativa um cupom: deixa de valer na hora. Já desativado responde 200 sem mudar nada.</summary>
    /// <param name="id">Cupom.</param>
    [HttpPost("{id:guid}/desativar")]
    [ProducesResponseType(typeof(CupomDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken ct) =>
        Responder((await cupomService.Desativar(id, usuarioAtual.Id, ct)).Map(cupom => cupom.Adapt<CupomDTO>()));
}
