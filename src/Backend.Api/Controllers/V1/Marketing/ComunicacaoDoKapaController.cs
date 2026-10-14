using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.Extensions;
using Backend.Business.Marketing.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Marketing;

/// <summary>
/// "Receber novidades do Kapa" (Sprint 40): descadastro pelo rodapé ou pelo cliente de e-mail.
/// </summary>
/// <remarks>
/// O consentimento é dado no cadastro. A retirada é feita pelo e-mail, sem login, ou pelo suporte.
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
