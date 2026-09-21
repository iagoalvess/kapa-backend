using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Auditoria;
using Backend.Api.DTOs.Comum;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Auditoria;

/// <summary>
/// Quem fez o quê com o dinheiro da turma.
/// </summary>
/// <remarks>
/// A tela existe para a assembleia poder perguntar "quem baixou esta parcela sem comprovante?" e
/// obter a resposta na hora, sem ninguém abrir o banco.
/// <para>
/// <b>Só leitura, e só da Gestão.</b> Formando recebe 403: a trilha nomeia quem fez cada operação
/// financeira da turma, e isso é material de prestação de contas da comissão, não painel público —
/// o público é o dashboard da Sprint 12, onde tudo é soma e nada aponta para uma pessoa.
/// </para>
/// <para>
/// Não existe rota de escrita aqui, e não deve existir: auditoria que a aplicação sabe escrever sob
/// demanda é auditoria que ela sabe forjar. A gravação acontece dentro da transação de cada
/// operação auditada.
/// </para>
/// </remarks>
/// <param name="auditoriaService">A trilha.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Auditoria)]
[Route("api/v{version:apiVersion}/auditoria")]
[Authorize(Policy = Politicas.Gestao)]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class AuditoriaController(IAuditoriaService auditoriaService) : MainController
{
    /// <summary>A trilha da turma, da mais recente, paginada e filtrada.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Período, autor, tipo de evento e busca por nome ou texto.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PaginaDTO<LinhaDeAuditoriaDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar([FromQuery] PaginacaoRequest paginacao, [FromQuery] FiltroDeAuditoriaDTO filtro, CancellationToken ct)
    {
        var resultado = await auditoriaService.Listar(
            FormaturaId,
            paginacao,
            new FiltroDeAuditoria(filtro.De, filtro.Ate, filtro.Autor, filtro.Nome, filtro.Busca),
            ct
        );

        return Responder(resultado.Map(pagina => pagina.ParaDTO(linha => linha.Adapt<LinhaDeAuditoriaDTO>())));
    }

    /// <summary>
    /// O que os seletores de filtro da tela oferecem: autores e tipos de evento desta turma.
    /// </summary>
    /// <remarks>
    /// Endpoint próprio em vez de a tela deduzir da página corrente — o seletor precisa dos autores
    /// da turma inteira, e não só dos que aparecem nas vinte linhas visíveis.
    /// </remarks>
    [HttpGet("opcoes-de-filtro")]
    [ProducesResponseType(typeof(OpcoesDeAuditoriaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Opcoes(CancellationToken ct) =>
        Responder((await auditoriaService.Opcoes(FormaturaId, ct)).Map(opcoes => opcoes.Adapt<OpcoesDeAuditoriaDTO>()));

    /// <summary>Os números do topo da tela: quantas ações, quando foi a última e quem mais fez.</summary>
    /// <remarks>
    /// Da turma inteira, sem os filtros da tela — a faixa diz como está a trilha, e o recorte
    /// filtrado já tem a própria contagem ao lado da busca.
    /// </remarks>
    [HttpGet("resumo")]
    [ProducesResponseType(typeof(ResumoDaAuditoriaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Resumo(CancellationToken ct) =>
        Responder((await auditoriaService.Resumir(FormaturaId, ct)).Map(resumo => resumo.Adapt<ResumoDaAuditoriaDTO>()));
}
