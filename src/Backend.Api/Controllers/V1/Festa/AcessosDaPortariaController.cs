using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Festa;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Festa.Interfaces;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Festa;

/// <summary>
/// A portaria terceirizada: quem fica na porta recebe um link, e não uma conta (15/10/2026).
/// </summary>
/// <remarks>
/// A Gestão libera, troca o link e corta. A ativação é anônima — o link é a prova —, fica fora do módulo e
/// devolve a credencial que o celular usa nas rotas da <see cref="PortariaController"/>.
/// </remarks>
/// <param name="acessos">Regras dos acessos.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/festa/portaria/acessos")]
public sealed class AcessosDaPortariaController(IAcessosDaPortariaService acessos, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>As pessoas liberadas para a portaria do evento.</summary>
    /// <param name="eventoId">Evento.</param>
    [HttpGet]
    [ExigeModulo(Modulo.Festa)]
    [Authorize(Policy = Politicas.Gestao)]
    [EnableRateLimiting(RateLimitConfig.Padrao)]
    [ProducesResponseType(typeof(IReadOnlyList<AcessoDaPortariaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Listar([FromQuery] Guid eventoId, CancellationToken ct) =>
        Responder((await acessos.Listar(eventoId, ct)).Map(lista => lista.Adapt<IReadOnlyList<AcessoDaPortariaDTO>>()));

    /// <summary>Libera uma pessoa para a portaria: devolve o acesso com o segredo do link.</summary>
    /// <remarks>Evento cuja validação já fechou responde 409 <c>festa.evento_encerrado</c>.</remarks>
    /// <param name="requisicao">Evento e nome.</param>
    [HttpPost]
    [ExigeModulo(Modulo.Festa)]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [EnableRateLimiting(RateLimitConfig.Padrao)]
    [RegistrarEvento("festa.portaria_liberada")]
    [ProducesResponseType(typeof(AcessoDaPortariaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Liberar([FromBody] AcessoDaPortariaRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            (await acessos.Liberar(requisicao.EventoId, requisicao.Nome, usuarioAtual.Id, ct)).Map(acesso => acesso.Adapt<AcessoDaPortariaDTO>())
        );

    /// <summary>Corta o acesso e gera outro link para a mesma pessoa.</summary>
    /// <param name="id">Acesso.</param>
    [HttpPost("{id:guid}/novo-link")]
    [ExigeModulo(Modulo.Festa)]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [EnableRateLimiting(RateLimitConfig.Padrao)]
    [RegistrarEvento("festa.portaria_novo_link", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(AcessoDaPortariaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> NovoLink(Guid id, CancellationToken ct) =>
        Responder((await acessos.NovoLink(id, usuarioAtual.Id, ct)).Map(acesso => acesso.Adapt<AcessoDaPortariaDTO>()));

    /// <summary>Corta o acesso: o celular para de validar na requisição seguinte.</summary>
    /// <remarks>Sem a política de status de propósito: turma suspensa ainda tira alguém da porta.</remarks>
    /// <param name="id">Acesso.</param>
    [HttpDelete("{id:guid}")]
    [ExigeModulo(Modulo.Festa)]
    [Authorize(Policy = Politicas.Gestao)]
    [EnableRateLimiting(RateLimitConfig.Padrao)]
    [RegistrarEvento("festa.portaria_cortada", CamposDaRota = ["id"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revogar(Guid id, CancellationToken ct) => Responder(await acessos.Revogar(id, ct));

    /// <summary>Abre o link no celular de quem fica na porta: gasta o link e devolve a credencial.</summary>
    /// <remarks>
    /// Anônimo. Link gasto, cortado ou de evento encerrado: 404 <c>festa.link_da_portaria_invalido</c>.
    /// </remarks>
    /// <param name="requisicao">O segredo do link.</param>
    [HttpPost("ativar")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitConfig.Codigo)]
    [ProducesResponseType(typeof(CredencialDaPortariaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Ativar([FromBody] AtivacaoDaPortariaRequestDTO requisicao, CancellationToken ct) =>
        Responder((await acessos.Ativar(requisicao.Token, ct)).Map(credencial => credencial.Adapt<CredencialDaPortariaDTO>()));
}
