using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Notificacoes;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Services;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;
using Backend.Business.Notificacoes.Services;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Notificacoes;

/// <summary>
/// A régua de cobrança: a tesouraria configura e testa, a gestão audita e o membro escolhe o que recebe.
/// </summary>
/// <remarks>
/// Cada rota tem o recorte que a sprint pede: configurar e disparar são da Tesouraria, o histórico é
/// da Gestão — é o que ela mostra quando alguém diz "nunca fui avisado" — e a preferência é do
/// próprio titular, lida e gravada pelo vínculo dele.
/// <para>
/// <c>testar</c> manda para quem clicou, nunca para a turma: o destinatário sai da conta autenticada
/// e não existe campo de e-mail no corpo.
/// </para>
/// </remarks>
/// <param name="notificacaoService">Regras da régua.</param>
/// <param name="usuarioAtual">Quem chama.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/notificacoes")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class NotificacaoController(INotificacaoService notificacaoService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>A régua da turma. Quem nunca configurou recebe a padrão, já gravada.</summary>
    [HttpGet("regras")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [ProducesResponseType(typeof(ReguaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterRegras(CancellationToken ct) => Responder((await notificacaoService.ListarRegras(ct)).Map(Regua));

    /// <summary>Grava a régua inteira. Variável desconhecida é recusada aqui, com 400.</summary>
    /// <param name="requisicao">Os degraus.</param>
    [HttpPut("regras")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("notificacao.regua_alterada")]
    [ProducesResponseType(typeof(ReguaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SalvarRegras([FromBody] ReguaRequestDTO requisicao, CancellationToken ct) =>
        Responder((await notificacaoService.SalvarRegras(Dados(requisicao), ct)).Map(Regua));

    /// <summary>Manda o degrau com dados de exemplo para o e-mail de quem clicou. A turma não recebe nada.</summary>
    /// <param name="id">Degrau a testar.</param>
    [HttpPost("regras/{id:guid}/testar")]
    [Authorize(Policy = Politicas.Tesouraria)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [RegistrarEvento("notificacao.regra_testada", CamposDaRota = ["id"])]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Testar(Guid id, CancellationToken ct) =>
        Responder(await notificacaoService.Testar(FormaturaId, usuarioAtual.Id, id, ct));

    /// <summary>Quem recebeu o quê, quando, por qual canal e com qual resultado.</summary>
    /// <param name="paginacao">Página e ordenação.</param>
    /// <param name="filtro">Canal, situação, período e busca.</param>
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

    /// <summary>O que o próprio membro escolheu receber.</summary>
    [HttpGet("preferencias/eu")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(IReadOnlyList<PreferenciaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MinhasPreferencias(CancellationToken ct) =>
        Responder((await notificacaoService.ListarPreferencias(FormaturaId, usuarioAtual.Id, ct)).Map(Preferencias));

    /// <summary>Grava as escolhas do próprio membro. Desligar a cobrança devolve 409.</summary>
    /// <param name="requisicao">Um item por tipo.</param>
    [HttpPut("preferencias/eu")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [Authorize(Policy = Politicas.ExigeFormaturaAtiva)]
    [ProducesResponseType(typeof(IReadOnlyList<PreferenciaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SalvarPreferencias([FromBody] PreferenciasRequestDTO requisicao, CancellationToken ct)
    {
        var dados = new DadosDasPreferencias([.. (requisicao.Preferencias ?? []).Select(p => new PreferenciaEscolhida(p.Tipo, p.Ativa))]);

        return Responder((await notificacaoService.SalvarPreferencias(FormaturaId, usuarioAtual.Id, dados, ct)).Map(Preferencias));
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

    private static ReguaDTO Regua(IReadOnlyList<RegraResumo> regras) =>
        new(
            [.. regras.Select(regra => regra.Adapt<RegraDTO>())],
            TemplateDeNotificacao.Variaveis,
            TemplateDeNotificacao.TamanhoMaximo,
            TemplateDeNotificacao.TamanhoMaximoDoAssunto
        );

    private static IReadOnlyList<PreferenciaDTO> Preferencias(IReadOnlyList<PreferenciaResumo> preferencias) =>
        [.. preferencias.Select(preferencia => preferencia.Adapt<PreferenciaDTO>())];

    private static DadosDaRegua Dados(ReguaRequestDTO requisicao) =>
        new([
            .. (requisicao.Regras ?? []).Select(regra => new DadosDaRegra(
                regra.Gatilho,
                regra.DiasDeDeslocamento,
                regra.Canal,
                regra.Assunto ?? string.Empty,
                regra.Template ?? string.Empty,
                regra.Ativa,
                regra.AvisarTesouraria
            )),
        ]);
}
