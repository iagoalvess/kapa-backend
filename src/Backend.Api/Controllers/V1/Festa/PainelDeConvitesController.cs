using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Festa;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Agenda.Models;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Festa;

/// <summary>
/// O painel de convites de um evento — a festa ou a colação: o que a Gestão vê no evento da agenda.
/// </summary>
/// <remarks>
/// Sucessor da cota da Sprint 30 (Sprint 47, D15): os convites vêm dos pacotes das cestas e saem sozinhos, na adesão
/// e quando o evento fica completo na agenda — não há mais "abrir". O evento vem por <c>?tipo=</c>, como em "Meus
/// convites" e na portaria. Outro tipo de evento responde 400 <c>festa.evento_sem_convite</c>.
/// </remarks>
/// <param name="painel">O painel do evento.</param>
/// <param name="usuarioAtual">Quem libera, para a auditoria.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Festa)]
[Route("api/v{version:apiVersion}/festa/painel-de-convites")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
[Authorize(Policy = Politicas.Gestao)]
public sealed class PainelDeConvitesController(IPainelDeConvitesService painel, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>O painel: capacidade, a conta de lugares e quem está preso por atraso.</summary>
    /// <param name="tipo">Festa (o padrão) ou colação.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PainelDeConvitesDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter([FromQuery] TipoDeEvento tipo = TipoDeEvento.Festa, CancellationToken ct = default) =>
        Responder((await painel.Obter(tipo, ct)).Map(resultado => resultado.Adapt<PainelDeConvitesDTO>()));

    /// <summary>Grava a capacidade do local. Passar dela avisa no painel e salva.</summary>
    /// <param name="requisicao">Lugares.</param>
    /// <param name="tipo">Festa (o padrão) ou colação.</param>
    [HttpPut("capacidade")]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(PainelDeConvitesDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DefinirCapacidade(
        [FromBody] CapacidadeRequestDTO requisicao,
        [FromQuery] TipoDeEvento tipo = TipoDeEvento.Festa,
        CancellationToken ct = default
    ) =>
        Responder(
            (await painel.DefinirCapacidade(tipo, new DadosDaCapacidade(requisicao.Capacidade), ct)).Map(resultado =>
                resultado.Adapt<PainelDeConvitesDTO>()
            )
        );

    /// <summary>
    /// Solta os convites de pacote de um formando presos por parcela em atraso, em todos os eventos (D24).
    /// </summary>
    /// <param name="vinculoId">O formando, como o painel o lista.</param>
    [HttpPost("presos/{vinculoId:guid}/liberacao")]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(LiberacaoDeConvitesDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Liberar(Guid vinculoId, CancellationToken ct = default) =>
        Responder((await painel.Liberar(vinculoId, usuarioAtual.Id, ct)).Map(liberados => new LiberacaoDeConvitesDTO(liberados)));
}
