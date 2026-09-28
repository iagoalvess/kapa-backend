using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Busca;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Busca.Interfaces;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Busca;

/// <summary>
/// A busca do topo da tela: uma caixa, tudo o que a turma tem.
/// </summary>
/// <remarks>
/// Um endpoint, e não uma busca por tela: quem procura "Gabriela" não sabe se ela está na lista de
/// membros ou no aviso do mural, e não deveria precisar saber.
/// <para>
/// A política é <c>MembroDaFormatura</c>, o piso — o recorte do que cada papel enxerga acontece
/// dentro da consulta, e não com cinco endpoints e cinco políticas. O papel sai do vínculo, no
/// service — a claim do token ficaria até 15 minutos atrasada depois de um rebaixamento.
/// </para>
/// </remarks>
/// <param name="buscaService">A busca.</param>
/// <param name="usuarioAtual">Identidade da requisição.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/busca")]
[Authorize(Policy = Politicas.MembroDaFormatura)]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class BuscaController(IBuscaService buscaService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>O que casa com o termo, agrupado.</summary>
    /// <remarks>Termo com menos de três caracteres devolve tudo vazio: quem está digitando não errou nada.</remarks>
    /// <param name="termo">O que a pessoa digitou.</param>
    [HttpGet]
    [ProducesResponseType(typeof(BuscaNaTurmaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Buscar([FromQuery] string? termo, CancellationToken ct)
    {
        return Responder((await buscaService.Buscar(FormaturaId, usuarioAtual.Id, termo, ct)).Map(busca => busca.Adapt<BuscaNaTurmaDTO>()));
    }
}
