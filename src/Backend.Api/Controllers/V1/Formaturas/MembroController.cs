using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Formaturas;
using Backend.Business.Abstractions;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Formaturas;

/// <summary>
/// Membros da formatura selecionada: quem são, que papel têm, e quem sai.
/// </summary>
/// <remarks>
/// A rota diz <c>atual</c>, e não o id: a formatura vem da claim do token, nunca de um valor que
/// o cliente escolhe.
/// </remarks>
/// <param name="membroService">Gestão de membros.</param>
/// <param name="formaturaAtual">Formatura da sessão.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/formaturas/atual/membros")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class MembroController(IMembroService membroService, IFormaturaAtual formaturaAtual) : MainController
{
    /// <summary>A política garante a claim; o <c>Guid.Empty</c> nunca chega a ser consultado.</summary>
    private Guid FormaturaId => formaturaAtual.Id ?? Guid.Empty;

    /// <summary>Lista os vínculos da formatura, paginados, com papel e situação.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="busca">Trecho do nome ou do e-mail.</param>
    /// <param name="ativo"><c>true</c> só ativos, <c>false</c> só removidos; ausente traz todos.</param>
    /// <param name="papel">Só este papel; ausente traz todos.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(PaginaDTO<MembroDaFormaturaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] string? busca,
        [FromQuery] bool? ativo,
        [FromQuery] string? papel,
        CancellationToken ct
    )
    {
        var resultado = await membroService.Listar(FormaturaId, paginacao.ParaModelo(), new FiltroDeMembros(busca, ativo, papel), ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(membro => membro.Adapt<MembroDaFormaturaDTO>())));
    }

    /// <summary>Quantos membros a formatura tem em cada papel e situação — os números da tela de membros.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("resumo")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(IReadOnlyList<ContagemDeMembrosDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resumir(CancellationToken ct)
    {
        var resultado = await membroService.Contar(FormaturaId, ct);

        return Responder(resultado.Map(contagens => contagens.Adapt<List<ContagemDeMembrosDTO>>()));
    }

    /// <summary>Altera o papel de um membro ativo.</summary>
    /// <param name="usuarioId">Membro a alterar.</param>
    /// <param name="requisicao">Papel novo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPut("{usuarioId:guid}/papel")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaEditavel)]
    [RegistrarEvento("membro.papel_alterado", CamposDaRota = ["usuarioId"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AlterarPapel(Guid usuarioId, [FromBody] AlterarPapelRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await membroService.AlterarPapel(FormaturaId, usuarioId, new AlterarPapel(requisicao.Papel), ct);

        return Responder(resultado);
    }

    /// <summary>Desativa o vínculo de um membro. O histórico financeiro dele permanece.</summary>
    /// <param name="usuarioId">Membro a remover.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpDelete("{usuarioId:guid}")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaEditavel)]
    [RegistrarEvento("membro.removido", CamposDaRota = ["usuarioId"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remover(Guid usuarioId, CancellationToken ct)
    {
        var resultado = await membroService.Remover(FormaturaId, usuarioId, ct);

        return Responder(resultado);
    }
}
