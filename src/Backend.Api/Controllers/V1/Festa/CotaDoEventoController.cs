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
/// A cota de convites de um evento — a festa ou a colação: o painel que a Gestão vê no evento da agenda.
/// </summary>
/// <remarks>
/// Da Gestão, como a portaria. Nasceu só para a colação (Sprint 30) e vale para a festa desde
/// 01/10/2026: o evento vem por <c>?tipo=</c>, como em "Meus convites" e na portaria. O convite que a
/// abertura emite é o da Sprint 21 — página, PDF, check-in e revogação continuam em
/// <see cref="ConviteDaFestaController"/> e <see cref="PortariaController"/>. Outro tipo de evento
/// responde 400 <c>festa.evento_sem_convite</c>.
/// </remarks>
/// <param name="cota">Regras da cota.</param>
/// <param name="usuarioAtual">Quem abre, para a auditoria.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Festa)]
[Route("api/v{version:apiVersion}/festa/cota")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
[Authorize(Policy = Politicas.Gestao)]
public sealed class CotaDoEventoController(ICotaDoEventoService cota, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>O painel: cota, capacidade e a conta aberta.</summary>
    /// <param name="tipo">Festa (o padrão) ou colação.</param>
    /// <remarks>Sem o evento na agenda, 404 <c>agenda.evento_nao_encontrado</c>.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PainelDaCotaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter([FromQuery] TipoDeEvento tipo = TipoDeEvento.Festa, CancellationToken ct = default) =>
        Responder((await cota.Obter(tipo, ct)).Map(painel => painel.Adapt<PainelDaCotaDTO>()));

    /// <summary>Grava a cota e a capacidade. Passar da capacidade avisa no painel e salva.</summary>
    /// <remarks>Depois de aberta, a cota só sobe: 409 <c>festa.cota_ja_aberta</c>.</remarks>
    /// <param name="requisicao">Cota por formando e capacidade.</param>
    /// <param name="tipo">Festa (o padrão) ou colação.</param>
    [HttpPut]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(PainelDaCotaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Definir(
        [FromBody] CotaRequestDTO requisicao,
        [FromQuery] TipoDeEvento tipo = TipoDeEvento.Festa,
        CancellationToken ct = default
    ) =>
        Responder(
            (await cota.Definir(tipo, new DadosDaCota(requisicao.CotaPorFormando, requisicao.Capacidade), ct)).Map(painel =>
                painel.Adapt<PainelDaCotaDTO>()
            )
        );

    /// <summary>Abre ou reabre a cota: emite o que falta a cada formando ativo. Reabrir não duplica.</summary>
    /// <remarks>Sem cota, 409 <c>festa.cota_nao_configurada</c>; sem hora e local, 409 <c>festa.evento_incompleto</c>.</remarks>
    /// <param name="tipo">Festa (o padrão) ou colação.</param>
    [HttpPost("abrir")]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(PainelDaCotaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Abrir([FromQuery] TipoDeEvento tipo = TipoDeEvento.Festa, CancellationToken ct = default) =>
        Responder((await cota.Abrir(tipo, usuarioAtual.Id, ct)).Map(painel => painel.Adapt<PainelDaCotaDTO>()));
}
