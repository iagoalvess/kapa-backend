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
/// Leitura da gestão inteira — Presidente, Tesoureiro e Comissão —, porque é com este número que a
/// comissão decide contratar. Só leitura: nada aqui grava, e saldo não é coluna (decisão 1).
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

    /// <summary>O fluxo mês a mês: realizado até hoje, projetado até a colação.</summary>
    /// <remarks>Parcela vencida não entra em mês nenhum: vai em <c>emAtrasoEmCentavos</c>, à parte (decisão 6).</remarks>
    [HttpGet("projecao")]
    [Authorize(Policy = Politicas.Gestao)]
    [ProducesResponseType(typeof(ProjecaoDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Projecao(CancellationToken ct) =>
        Responder((await caixaService.Projecao(FormaturaId, ct)).Map(projecao => projecao.Adapt<ProjecaoDTO>()));
}
