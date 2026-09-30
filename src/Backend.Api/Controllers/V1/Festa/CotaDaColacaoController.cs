using Asp.Versioning;
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
/// A cota de convites da colação: o painel que a Gestão vê no evento da agenda (Sprint 30).
/// </summary>
/// <remarks>
/// Da Gestão, como a portaria. O convite que a abertura emite é o da Sprint 21 — página, PDF,
/// check-in e revogação continuam em <see cref="ConviteDaFestaController"/> e <see cref="PortariaController"/>.
/// </remarks>
/// <param name="cota">Regras da cota.</param>
/// <param name="usuarioAtual">Quem abre, para a auditoria.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Festa)]
[Route("api/v{version:apiVersion}/festa/colacao/cota")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
[Authorize(Policy = Politicas.Gestao)]
public sealed class CotaDaColacaoController(ICotaDoEventoService cota, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>O painel: cota, capacidade e a conta aberta.</summary>
    /// <remarks>Sem colação na agenda, 404 <c>agenda.evento_nao_encontrado</c>.</remarks>
    [HttpGet]
    [ProducesResponseType(typeof(PainelDaCotaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(CancellationToken ct) => Responder((await cota.Obter(ct)).Map(painel => painel.Adapt<PainelDaCotaDTO>()));

    /// <summary>Grava a cota e a capacidade. Passar da capacidade avisa no painel e salva.</summary>
    /// <remarks>Depois de aberta, a cota só sobe: 409 <c>festa.cota_ja_aberta</c>.</remarks>
    /// <param name="requisicao">Cota por formando e capacidade.</param>
    [HttpPut]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(PainelDaCotaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Definir([FromBody] CotaRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (await cota.Definir(new DadosDaCota(requisicao.CotaPorFormando, requisicao.Capacidade), ct)).Map(painel =>
                painel.Adapt<PainelDaCotaDTO>()
            )
        );

    /// <summary>Abre ou reabre a cota: emite o que falta a cada formando ativo. Reabrir não duplica.</summary>
    /// <remarks>Sem cota, 409 <c>festa.cota_nao_configurada</c>; sem hora e local, 409 <c>festa.evento_incompleto</c>.</remarks>
    [HttpPost("abrir")]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(PainelDaCotaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Abrir(CancellationToken ct) =>
        Responder((await cota.Abrir(usuarioAtual.Id, ct)).Map(painel => painel.Adapt<PainelDaCotaDTO>()));
}
