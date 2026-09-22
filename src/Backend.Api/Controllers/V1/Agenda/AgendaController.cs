using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Agenda;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Agenda;

/// <summary>
/// As datas da turma: colação, festa, reunião, prazo.
/// </summary>
/// <remarks>
/// Leitura para todo membro, escrita para a Gestão (P3) — marcar reunião não é decisão de
/// presidente, e é a mesma regra do mural e dos itens da festa.
/// <para>
/// <b>Sem <c>ExigeModulo</c></b> (P6): a agenda é de toda turma, como a tela da festa. Plano sem a
/// data da própria colação é produto quebrado, não produto barato.
/// </para>
/// </remarks>
/// <param name="agenda">Regras dos eventos da turma.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/agenda")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class AgendaController(IAgendaService agenda) : MainController
{
    /// <summary>Nome da rota do evento, para o <c>Location</c> da criação.</summary>
    public const string RotaDoEvento = "EventoDaAgendaPorId";

    /// <summary>A agenda da turma inteira, do evento mais antigo para o mais novo.</summary>
    /// <remarks>Sem paginação: quem agrupa por mês e esconde o que já passou é a tela (decisão 10).</remarks>
    [HttpGet]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(IEnumerable<EventoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Responder((await agenda.Listar(ct)).Map(eventos => eventos.Select(evento => evento.Adapt<EventoDTO>())));

    /// <summary>As próximas datas da turma e quantas ainda vêm — o bloco da Página Inicial.</summary>
    /// <remarks>
    /// Existe para a home não pedir a agenda inteira em toda abertura do app só para desenhar três
    /// linhas. Cancelado fica de fora: ali o espaço é do que vai acontecer.
    /// </remarks>
    [HttpGet("resumo")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(ResumoDaAgendaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resumir(CancellationToken ct) =>
        Responder(
            (await agenda.Resumir(ct)).Map(resumo => new ResumoDaAgendaDTO(resumo.Proximos.Select(evento => evento.Adapt<EventoDTO>()), resumo.Total))
        );

    /// <summary>Um evento da agenda.</summary>
    /// <param name="id">Evento.</param>
    [HttpGet("{id:guid}", Name = RotaDoEvento)]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(EventoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct) =>
        Responder((await agenda.ObterPorId(id, ct)).Map(evento => evento.Adapt<EventoDTO>()));

    /// <summary>Marca uma data nova.</summary>
    /// <remarks>Segunda colação ou segunda festa devolvem 409 <c>agenda.tipo_unico</c>.</remarks>
    /// <param name="requisicao">Título, tipo, situação, dia, hora, local e descrição.</param>
    [HttpPost]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("agenda.evento_criado")]
    [ProducesResponseType(typeof(EventoDTO), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Criar([FromBody] EventoRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await agenda.Criar(ParaModelo(requisicao), ct);

        return Criado(
            resultado.Map(evento => evento.Adapt<EventoDTO>()),
            RotaDoEvento,
            new { id = resultado.Sucesso ? resultado.Valor.Id : Guid.Empty }
        );
    }

    /// <summary>Corrige uma data.</summary>
    /// <remarks>
    /// Cancelar é este mesmo endpoint, com a situação: aqui — ao contrário do item da festa — não há
    /// nada a preservar do outro lado, então não vale uma rota própria.
    /// </remarks>
    /// <param name="id">Evento.</param>
    /// <param name="requisicao">Dados novos.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("agenda.evento_alterado", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(EventoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] EventoRequestDTO requisicao, CancellationToken ct) =>
        Responder((await agenda.Atualizar(id, ParaModelo(requisicao), ct)).Map(evento => evento.Adapt<EventoDTO>()));

    /// <summary>Tira o evento da agenda.</summary>
    /// <remarks>Excluir é para o que foi digitado errado; o que a turma desmarcou fica na lista, cancelado.</remarks>
    /// <param name="id">Evento.</param>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Politicas.Gestao)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("agenda.evento_excluido", CamposDaRota = ["id"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken ct) => Responder(await agenda.Excluir(id, ct));

    /// <summary>O corpo como o service o espera; situação ausente é <c>AConfirmar</c>.</summary>
    private static DadosDoEvento ParaModelo(EventoRequestDTO requisicao) =>
        new(
            requisicao.Titulo ?? string.Empty,
            requisicao.Tipo,
            requisicao.Situacao ?? SituacaoDoEvento.AConfirmar,
            requisicao.Data,
            requisicao.Hora,
            requisicao.Local,
            requisicao.Descricao
        );
}
