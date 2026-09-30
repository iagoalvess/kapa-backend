using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Marketing;
using Backend.Api.Extensions;
using Backend.Business.Marketing.Interfaces;
using Backend.Business.Marketing.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Marketing;

/// <summary>
/// "Receber novidades do Kapa" (Sprint 40): a preferência da conta e o descadastro de um clique do e-mail.
/// </summary>
/// <remarks>
/// Divide o prefixo <c>privacidade</c> com o portal do titular — é lá que a pessoa liga e desliga —, mas o assunto
/// é o marketing do Kapa. Por conta, e não por turma: nada aqui depende da formatura selecionada.
/// </remarks>
/// <param name="comunicacaoDoKapa">A preferência de marketing do Kapa e o descadastro de um clique.</param>
/// <param name="usuarioAtual">Quem está fazendo a requisição — é o titular, sempre.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/privacidade")]
[Authorize(Policy = Politicas.Autenticado)]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class ComunicacaoDoKapaController(IComunicacaoDoKapaService comunicacaoDoKapa, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>
    /// Liga ou desliga "Receber novidades do Kapa" (Sprint 40).
    /// </summary>
    /// <remarks>
    /// Por conta, e não por turma. Grava uma linha no histórico só quando o valor muda — pedir o que já vale
    /// responde 204 e não registra nada.
    /// </remarks>
    /// <param name="requisicao">O valor novo.</param>
    [HttpPut("comunicacao-do-kapa")]
    [RegistrarEvento("privacidade.comunicacao_do_kapa_alterada")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DefinirComunicacaoDoKapa([FromBody] ComunicacaoDoKapaRequestDTO requisicao, CancellationToken ct) =>
        Responder(
            await comunicacaoDoKapa.DefinirPreferencia(
                usuarioAtual.Id,
                requisicao.Receber,
                OrigemDoConsentimentoDeMarketing.MinhaPrivacidade,
                usuarioAtual.Origem,
                ct
            )
        );

    /// <summary>
    /// O descadastro de um clique do e-mail de marketing — sem login.
    /// </summary>
    /// <remarks>
    /// Atende os dois caminhos: o <c>POST</c> que o cliente de e-mail faz sozinho pelo <c>List-Unsubscribe-Post</c>
    /// (RFC 8058, corpo <c>List-Unsubscribe=One-Click</c>, ignorado) e o botão da página do app. O token vem na
    /// query nos dois.
    /// <para>
    /// Responde 204 para token válido, vencido ou adulterado: endpoint anônimo não confirma existência de conta.
    /// <c>POST</c> e nunca <c>GET</c> — antivírus de e-mail visitam os links, e sair não pode depender de ninguém
    /// ter aberto a mensagem.
    /// </para>
    /// </remarks>
    /// <param name="token">O token do link.</param>
    [HttpPost("descadastro")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitConfig.Webhook)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Descadastrar([FromQuery] string? token, CancellationToken ct) =>
        Responder(await comunicacaoDoKapa.Descadastrar(token, usuarioAtual.Origem, ct));
}
