using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Financeiro;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Financeiro.Interfaces;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Financeiro;

/// <summary>
/// Quanto a turma tem, quanto ainda entra e quanto ainda sai.
/// </summary>
/// <remarks>
/// O consolidado e a arrecadação são de todo membro — prestação de contas, só somas; a projeção, com o
/// planejamento das despesas, é da gestão, porque é com ela que a comissão decide contratar. O menu esconde
/// o Caixa do formando, mas a leitura pela API é dele por direito. Só leitura: nada aqui grava, e saldo não
/// é coluna (decisão 1).
/// </remarks>
/// <param name="caixaService">Agregações do caixa.</param>
[ApiVersion("1.0")]
[ExigeModulo(Modulo.Caixa)]
[Route("api/v{version:apiVersion}/financeiro/caixa")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class CaixaController(ICaixaService caixaService) : MainController
{
    /// <summary>O caixa de hoje: arrecadado, gasto, saldo, a receber, o quadro por categoria e os últimos lançamentos.</summary>
    /// <remarks>
    /// Todo membro lê, o formando inclusive: é a prestação de contas da turma, e quem paga tem direito
    /// de saber para onde o dinheiro foi. Tudo aqui é soma — nenhum número aponta para uma pessoa.
    /// </remarks>
    [HttpGet]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(CaixaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Consolidado(CancellationToken ct) =>
        Responder((await caixaService.Consolidado(ct)).Map(caixa => caixa.Adapt<CaixaDTO>()));

    /// <summary>Quanto a turma tinha juntado ao fim de cada um dos últimos cinco meses, e o previsto para o próximo.</summary>
    /// <remarks>
    /// Todo membro lê — é o gráfico do Início. Só entradas somadas: a projeção, com despesas e
    /// planejamento, continua da Gestão.
    /// </remarks>
    [HttpGet("arrecadacao")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(IReadOnlyList<MesDaArrecadacaoDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Arrecadacao(CancellationToken ct) =>
        Responder((await caixaService.Arrecadacao(ct)).Map(meses => meses.Adapt<IReadOnlyList<MesDaArrecadacaoDTO>>()));

    /// <summary>O fluxo mês a mês: realizado até hoje, projetado até a colação.</summary>
    /// <remarks>Parcela vencida não entra em mês nenhum: vai em <c>emAtrasoEmCentavos</c>, à parte (decisão 6).</remarks>
    [HttpGet("projecao")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(ProjecaoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Projecao(CancellationToken ct) =>
        Responder((await caixaService.Projecao(FormaturaId, ct)).Map(projecao => projecao.Adapt<ProjecaoDTO>()));
}
