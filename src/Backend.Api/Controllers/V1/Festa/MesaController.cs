using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Festa;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Festa;

/// <summary>
/// As mesas do jantar (Sprint 27).
/// </summary>
/// <remarks>
/// A comissão cadastra e atribui (P1); o formando só lê as dele. Toda escrita vira evento de negócio —
/// é a auditoria que a P4 pede para o ajuste depois do fechamento da lista, e ela vale para qualquer
/// hora, sem janela a conferir.
/// </remarks>
/// <param name="mesas">Regras das mesas.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/festa/mesas")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class MesaController(IMesaService mesas, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>O mapa: a faixa do topo, as mesas e quem comprou mesa.</summary>
    [HttpGet]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(MapaDeMesasDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Mapa(CancellationToken ct) => Responder((await mesas.Mapa(ct)).Map(mapa => mapa.Adapt<MapaDeMesasDTO>()));

    /// <summary>As mesas do próprio formando.</summary>
    [HttpGet("minhas")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(IReadOnlyList<MesaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListarMinhas(CancellationToken ct) =>
        Responder((await mesas.ListarMinhas(FormaturaId, usuarioAtual.Id, ct)).Map(lista => lista.Adapt<IReadOnlyList<MesaDTO>>()));

    /// <summary>Cadastra uma mesa.</summary>
    /// <param name="requisicao">Identificação, lugares, observação e reserva.</param>
    [HttpPost]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.mesa_criada")]
    [ProducesResponseType(typeof(MesaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Criar([FromBody] MesaRequestDTO requisicao, CancellationToken ct) =>
        Responder((await mesas.Criar(ParaModelo(requisicao), ct)).Map(mesa => mesa.Adapt<MesaDTO>()));

    /// <summary>Altera o cadastro da mesa.</summary>
    /// <param name="id">Mesa.</param>
    /// <param name="requisicao">Dados novos.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.mesa_alterada", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(MesaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] MesaRequestDTO requisicao, CancellationToken ct) =>
        Responder((await mesas.Atualizar(id, ParaModelo(requisicao), ct)).Map(mesa => mesa.Adapt<MesaDTO>()));

    /// <summary>Exclui uma mesa sem dono.</summary>
    /// <remarks>Com dono, 409 <c>festa.mesa_com_dono</c>.</remarks>
    /// <param name="id">Mesa.</param>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.mesa_excluida", CamposDaRota = ["id"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct) => Responder(await mesas.Excluir(id, ct));

    /// <summary>Atribui a mesa a quem a comprou, ou a solta.</summary>
    /// <remarks>
    /// Além do que o formando comprou, 409 <c>festa.mesas_alem_do_pedido</c>; mesa reservada, 409
    /// <c>festa.mesa_reservada</c>.
    /// </remarks>
    /// <param name="id">Mesa.</param>
    /// <param name="requisicao">Formando, ou nulo.</param>
    [HttpPut("{id:guid}/dono")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("festa.mesa_atribuida", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(MesaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DefinirDono(Guid id, [FromBody] DonoDaMesaRequestDTO requisicao, CancellationToken ct) =>
        Responder((await mesas.DefinirDono(id, requisicao.VinculoId, ct)).Map(mesa => mesa.Adapt<MesaDTO>()));

    private static DadosDaMesa ParaModelo(MesaRequestDTO requisicao) =>
        new(requisicao.Identificacao ?? string.Empty, requisicao.Lugares, requisicao.Observacao, requisicao.Reservada ?? false);
}
