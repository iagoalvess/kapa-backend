using System.Text.Json;
using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Assinaturas;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Assinaturas;

/// <summary>
/// O plano que vale para a turma agora e os módulos que ele libera (Sprint 45).
/// </summary>
/// <remarks>
/// Separado de <see cref="AssinaturaController"/> porque as perguntas são outras. A assinatura é da Gestão
/// e não existe no gratuito (404). O plano, todo membro lê, e sempre há um: é com ele que a tela tranca a
/// área fora do plano antes de chamar a API, em vez de mostrar o 403 como erro.
/// <para>
/// Leitura, e não autorização: quem recusa continua sendo <c>[ExigeModulo]</c> em cada área.
/// </para>
/// </remarks>
/// <param name="assinaturaService">O plano vigente.</param>
/// <param name="registrador">Fila de analytics, para o paywall exibido.</param>
/// <param name="usuario">Quem viu o paywall.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/formaturas/atual/plano")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class PlanoDaTurmaController(IAssinaturaService assinaturaService, IRegistradorDeEventos registrador, IUsuarioAtual usuario) : MainController
{
    /// <summary>O plano vigente da turma da sessão, com os módulos.</summary>
    /// <remarks>Aceita o desligado, como a moldura: é o menu dele também.</remarks>
    [HttpGet]
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [ProducesResponseType(typeof(PlanoDaTurmaDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Obter(CancellationToken ct) =>
        Responder((await assinaturaService.ObterPlanoDaTurma(FormaturaId, ct)).Map(plano => plano.Adapt<PlanoDaTurmaDTO>()));

    /// <summary>Registra que a turma viu o paywall: a área trancada ou o diálogo de upgrade.</summary>
    /// <remarks>
    /// Quem sabe que o paywall apareceu é a tela — a área trancada nem chama a API —, então é ela que avisa.
    /// É a etapa do funil entre "turma criada" e <c>assinatura.checkout_iniciado</c>, com a turma na coluna
    /// para contar turmas e não cliques. O motivo é o código do diálogo ou <c>modulo.{codigo}</c>; só
    /// minúscula, <c>_</c> e <c>.</c>, porque vem do cliente e vai para o banco.
    /// </remarks>
    /// <param name="motivo">Por que o paywall apareceu.</param>
    [HttpPost("paywall/{motivo}")]
    [Authorize(Policy = Politicas.TitularDoProprioHistorico)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult RegistrarPaywall(string motivo)
    {
        if (motivo.Length > 60 || !motivo.All(c => char.IsAsciiLetterLower(c) || c is '_' or '.'))
            return BadRequest();

        registrador.Registrar(
            new Evento
            {
                Nome = "plano.paywall_exibido",
                UsuarioId = usuario.Id,
                FormaturaId = FormaturaId,
                Dados = JsonSerializer.Serialize(new { motivo }),
            }
        );

        return NoContent();
    }
}
