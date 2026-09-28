using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Festa;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Agenda.Models;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Festa;

/// <summary>
/// A porta da festa: a lista, a validação de entrada e a lista sem rede.
/// </summary>
/// <remarks>
/// De qualquer membro da Gestão (P4); o formando recebe 403 em tudo. O limite é o padrão, por
/// usuário: a portaria não disputa a cota dos convidados abrindo o convite no Wi-Fi da casa. Convite
/// de outra turma responde 404, nunca 403 (decisão 15).
/// </remarks>
/// <param name="portaria">Regras da porta.</param>
/// <param name="usuarioAtual">Quem valida.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/festa")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
[Authorize(Policy = Politicas.Gestao)]
public sealed class PortariaController(IPortariaService portaria, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>A lista do evento, com a faixa de contagem e a busca por nome ou código.</summary>
    /// <param name="eventoId">Evento da portaria; ausente é o evento único do tipo.</param>
    /// <param name="tipo">Festa (o padrão) ou colação.</param>
    /// <param name="busca">Trecho do nome do convidado, de quem convidou ou do código.</param>
    [HttpGet("portaria")]
    [ProducesResponseType(typeof(ListaDaPortariaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? eventoId,
        [FromQuery] string? busca,
        CancellationToken ct,
        [FromQuery] TipoDeEvento tipo = TipoDeEvento.Festa
    ) => Responder((await portaria.Listar(eventoId, tipo, busca, ct)).Map(lista => lista.Adapt<ListaDaPortariaDTO>()));

    /// <summary>A lista em PDF, com o documento inteiro — a que o salão pede.</summary>
    /// <param name="eventoId">Evento; ausente é o evento único do tipo.</param>
    /// <param name="tipo">Festa (o padrão) ou colação.</param>
    [HttpGet("portaria/pdf")]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BaixarLista(
        [FromQuery] Guid? eventoId,
        CancellationToken ct,
        [FromQuery] TipoDeEvento tipo = TipoDeEvento.Festa
    ) => Arquivo(await portaria.ListaEmPdf(eventoId, tipo, ct));

    /// <summary>Um convite como a portaria o vê: situação, entrada e se a validação está aberta.</summary>
    /// <param name="codigo">Código digitado ou token do QR.</param>
    [HttpGet("portaria/convites/{codigo}")]
    [ProducesResponseType(typeof(ConsultaNaPortariaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Consultar(string codigo, CancellationToken ct) =>
        Responder((await portaria.Consultar(codigo, ct)).Map(consulta => consulta.Adapt<ConsultaNaPortariaDTO>()));

    /// <summary>Valida a entrada.</summary>
    /// <remarks>
    /// Segunda validação: 409 <c>festa.ja_validado</c>, com quem validou e quando em <c>dados</c>.
    /// Também 409: <c>festa.convite_revogado</c>, <c>festa.outro_evento</c>, <c>festa.fora_da_janela</c>
    /// (de 6 h antes a 12 h depois do horário) e <c>festa.convite_sem_titular</c>.
    /// </remarks>
    /// <param name="codigo">Código digitado ou token do QR.</param>
    /// <param name="requisicao">Evento aberto na portaria.</param>
    [HttpPost("convites/{codigo}/check-in")]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(EntradaNaPortariaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ValidarEntrada(string codigo, [FromBody] CheckInRequestDTO? requisicao, CancellationToken ct) =>
        Responder(
            (await portaria.ValidarEntrada(codigo, requisicao?.EventoId, usuarioAtual.Id, ct)).Map(entrada => entrada.Adapt<EntradaNaPortariaDTO>())
        );

    /// <summary>Desfaz uma entrada validada por engano.</summary>
    /// <param name="id">Entrada.</param>
    [HttpPost("check-ins/{id:guid}/desfazer")]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Desfazer(Guid id, CancellationToken ct) => Responder(await portaria.Desfazer(id, usuarioAtual.Id, ct));

    /// <summary>Sobe as entradas marcadas na lista sem rede. Nenhuma é descartada.</summary>
    /// <param name="requisicao">Entradas, com a hora e o aparelho de cada uma.</param>
    [HttpPost("check-ins/sincronizar")]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(ResultadoDaSincronizacaoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Sincronizar([FromBody] SincronizacaoRequestDTO requisicao, CancellationToken ct)
    {
        var entradas = (requisicao.Entradas ?? [])
            .Select(entrada => new EntradaSemRede(entrada.Codigo ?? string.Empty, entrada.ValidadoEm, entrada.Aparelho))
            .ToList();

        return Responder(
            (await portaria.Sincronizar(entradas, usuarioAtual.Id, ct)).Map(resultado => resultado.Adapt<ResultadoDaSincronizacaoDTO>())
        );
    }
}
