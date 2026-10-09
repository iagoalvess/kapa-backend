using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Notificacoes;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Notificacoes;

/// <summary>
/// A régua de cobrança: a tesouraria liga e desliga os degraus, a gestão audita e o membro escolhe o que recebe.
/// </summary>
/// <remarks>
/// Cada rota tem o recorte que a sprint pede: configurar e disparar são da Tesouraria, o histórico é
/// da Gestão — é o que ela mostra quando alguém diz "nunca fui avisado" — e a preferência é do
/// próprio titular, lida e gravada pelo vínculo dele.
/// </remarks>
/// <param name="notificacaoService">Regras da régua.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Avisos)]
[Route("api/v{version:apiVersion}/notificacoes")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class NotificacaoController(INotificacaoService notificacaoService) : MainController
{
    /// <summary>A régua da turma: os degraus do Kapa, cada um ligado ou não.</summary>
    [HttpGet("regras")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(ReguaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterRegras(CancellationToken ct) => Responder((await notificacaoService.ListarRegras(ct)).Map(Regua));

    /// <summary>Liga ou desliga um degrau.</summary>
    /// <param name="id">Degrau.</param>
    /// <param name="requisicao">Se dispara.</param>
    [HttpPut("regras/{id:guid}")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("notificacao.regua_alterada", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(ReguaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DefinirRegra(Guid id, [FromBody] RegraRequestDTO requisicao, CancellationToken ct) =>
        Responder((await notificacaoService.DefinirRegra(id, requisicao.Ativa, ct)).Map(Regua));

    /// <summary>Quem recebeu o quê, quando e com qual resultado.</summary>
    /// <param name="paginacao">Página e ordenação.</param>
    /// <param name="filtro">Situação, período e busca.</param>
    [HttpGet("historico")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(PaginaDTO<NotificacaoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Historico(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] FiltroDeNotificacoesDTO filtro,
        CancellationToken ct
    )
    {
        var resultado = await notificacaoService.ListarHistorico(paginacao.ParaModelo(), filtro.ParaModelo(), ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(item => item.Adapt<NotificacaoDTO>())));
    }

    /// <summary>Cobra uma parcela agora, à mão, com o degrau de atraso mais próximo.</summary>
    /// <param name="parcelaId">Parcela a cobrar.</param>
    [HttpPost("cobrar/{parcelaId:guid}")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("notificacao.cobranca_avulsa", CamposDaRota = ["parcelaId"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cobrar(Guid parcelaId, CancellationToken ct) =>
        Responder(await notificacaoService.Cobrar(FormaturaId, parcelaId, ct));

    private static ReguaDTO Regua(IReadOnlyList<RegraResumo> regras) => new([.. regras.Select(regra => regra.Adapt<RegraDTO>())]);
}
