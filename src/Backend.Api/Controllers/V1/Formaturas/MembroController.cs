using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Formaturas;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
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
/// <param name="usuarioAtual">Quem chama — o autor na trilha de auditoria da saída.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Membros)]
[Route("api/v{version:apiVersion}/formaturas/atual/membros")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class MembroController(IMembroService membroService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Lista os vínculos da formatura, paginados, com papel, situação e completude do cadastro.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="busca">Trecho do nome de exibição, do nome civil ou do e-mail.</param>
    /// <param name="ativo"><c>true</c> só ativos, <c>false</c> só quem saiu; ausente traz todos.</param>
    /// <param name="papel">Só este papel; ausente traz todos.</param>
    /// <param name="cadastro"><c>Pendente</c> (falta o essencial), <c>Incompleto</c> ou <c>Completo</c>; ausente traz todos.</param>
    /// <param name="desligado">
    /// <c>true</c> só desligados, <c>false</c> só quem nunca foi; ausente traz todos. Com
    /// <paramref name="ativo"/> <c>false</c>, é o que separa desligado de removido.
    /// </param>
    [HttpGet]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(PaginaDTO<MembroDaFormaturaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(
        [FromQuery] PaginacaoRequestDTO paginacao,
        [FromQuery] string? busca,
        [FromQuery] bool? ativo,
        [FromQuery] string? papel,
        [FromQuery] SituacaoDoCadastro? cadastro,
        [FromQuery] bool? desligado,
        CancellationToken ct
    )
    {
        var resultado = await membroService.Listar(
            FormaturaId,
            paginacao.ParaModelo(),
            new FiltroDeMembros(busca, ativo, papel, cadastro, desligado),
            ct
        );

        return Responder(resultado.Map(pagina => pagina.ParaDTO(membro => membro.Adapt<MembroDaFormaturaDTO>())));
    }

    /// <summary>Quantos membros a formatura tem em cada papel e situação — os números da tela de membros.</summary>
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
    [HttpPut("{usuarioId:guid}/papel")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AlterarPapel(Guid usuarioId, [FromBody] AlterarPapelRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await membroService.AlterarPapel(FormaturaId, usuarioId, new AlterarPapel(requisicao.Papel), usuarioAtual.Id, ct);

        return Responder(resultado);
    }

    /// <summary>Desativa o vínculo de um membro. O histórico financeiro dele permanece.</summary>
    /// <param name="usuarioId">Membro a remover.</param>
    [HttpDelete("{usuarioId:guid}")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remover(Guid usuarioId, CancellationToken ct)
    {
        var resultado = await membroService.Remover(FormaturaId, usuarioId, usuarioAtual.Id, ct);

        return Responder(resultado);
    }

    /// <summary>
    /// O que o desligamento de um membro vai mexer: o que ele já pagou, o que deve e o que está em atraso.
    /// </summary>
    /// <remarks>
    /// Alimenta o diálogo de confirmação. É <c>Gestao</c> e não <c>SomentePresidente</c> de propósito:
    /// o Tesoureiro vê e propõe, o Presidente executa (P3 de 17/09/2026).
    /// </remarks>
    /// <param name="usuarioId">Membro que sairia.</param>
    [HttpGet("{usuarioId:guid}/resumo-da-saida")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(ResumoDaSaidaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResumirSaida(Guid usuarioId, CancellationToken ct)
    {
        var resultado = await membroService.ResumirSaida(FormaturaId, usuarioId, ct);

        return Responder(resultado.Map(resumo => resumo.Adapt<ResumoDaSaidaDTO>()));
    }

    /// <summary>
    /// Desliga um formando: ele deixa de dever o que ainda não venceu, e os números da turma param
    /// de contar com ele.
    /// </summary>
    /// <remarks>
    /// Idempotente: desligar de novo devolve <c>formatura.membro_ja_desligado</c>, não cancela mais
    /// nada e não manda o segundo e-mail. Quem nunca aderiu devolve
    /// <c>formatura.membro_sem_adesao</c> — para ele a ação é Remover (decisão 1).
    /// </remarks>
    /// <param name="usuarioId">Membro a desligar.</param>
    /// <param name="requisicao">Motivo e o que fazer com o atraso.</param>
    [HttpPost("{usuarioId:guid}/desligar")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Desligar(Guid usuarioId, [FromBody] DesligarMembroRequestDTO requisicao, CancellationToken ct)
    {
        var dados = new DesligarFormando(requisicao.Motivo, requisicao.Detalhe, requisicao.CancelarAtraso);
        var resultado = await membroService.Desligar(FormaturaId, usuarioId, dados, usuarioAtual.Id, ct);

        return Responder(resultado);
    }
}
