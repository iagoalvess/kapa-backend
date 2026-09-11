using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Conta;
using Backend.Api.Extensions;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Conta;

/// <summary>
/// Ciclo de vida da conta: confirmação de e-mail e senha.
/// </summary>
/// <remarks>
/// Separado de <c>/auth</c>, que cuida da sessão. Aqui a conta é o objeto, e a maior parte das
/// operações acontece sem ninguém logado — é justamente quando o usuário não consegue entrar.
/// <para>
/// Todos os endpoints anônimos ficam sob o limite estreito de
/// <see cref="RateLimitConfig.Autenticacao"/>: sem ele, "esqueci minha senha" vira uma forma de
/// disparar e-mail em massa a partir do seu domínio, queimando a reputação de envio.
/// </para>
/// </remarks>
/// <param name="contaService">Regras do ciclo de vida da conta.</param>
/// <param name="usuarioAtual">Identidade da requisição.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/conta")]
[EnableRateLimiting(RateLimitConfig.Autenticacao)]
public sealed class ContaController(IContaService contaService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>
    /// Envia o e-mail com o link de redefinição de senha.
    /// </summary>
    /// <remarks>
    /// Responde 204 exista a conta ou não. Distinguir os dois casos transformaria o endpoint num
    /// verificador de quais e-mails têm cadastro.
    /// </remarks>
    /// <param name="requisicao">E-mail da conta.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [AllowAnonymous]
    [HttpPost("esqueci-senha")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EsqueciSenha([FromBody] PedidoPorEmailRequestDTO requisicao, CancellationToken ct) =>
        Responder(await contaService.SolicitarRedefinicaoDeSenha(new PedidoPorEmail(requisicao.Email), ct));

    /// <summary>Redefine a senha a partir do token recebido por e-mail.</summary>
    /// <param name="requisicao">E-mail, token e nova senha.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [RegistrarEvento("conta.senha_redefinida")]
    [AllowAnonymous]
    [HttpPost("redefinir-senha")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RedefinirSenha([FromBody] RedefinirSenhaRequestDTO requisicao, CancellationToken ct) =>
        Responder(await contaService.RedefinirSenha(new RedefinirSenha(requisicao.Email, requisicao.Token, requisicao.NovaSenha), ct));

    /// <summary>Confirma o e-mail a partir do token recebido.</summary>
    /// <param name="requisicao">E-mail e token.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [RegistrarEvento("conta.email_confirmado")]
    [AllowAnonymous]
    [HttpPost("confirmar-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmarEmail([FromBody] ConfirmarEmailRequestDTO requisicao, CancellationToken ct) =>
        Responder(await contaService.ConfirmarEmail(new ConfirmarEmail(requisicao.Email, requisicao.Token), ct));

    /// <summary>Reenvia o e-mail de confirmação. Responde 204 exista a conta ou não.</summary>
    /// <param name="requisicao">E-mail da conta.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [AllowAnonymous]
    [HttpPost("reenviar-confirmacao")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReenviarConfirmacao([FromBody] PedidoPorEmailRequestDTO requisicao, CancellationToken ct) =>
        Responder(await contaService.ReenviarConfirmacao(new PedidoPorEmail(requisicao.Email), ct));

    /// <summary>
    /// Troca a senha do usuário autenticado.
    /// </summary>
    /// <remarks>
    /// Exige a senha atual, e encerra todas as sessões abertas — inclusive esta. O cliente precisa
    /// autenticar de novo depois de uma troca bem-sucedida.
    /// </remarks>
    /// <param name="requisicao">Senha atual e nova senha.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [RegistrarEvento("conta.senha_alterada")]
    [HttpPost("alterar-senha")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AlterarSenha([FromBody] AlterarSenhaRequestDTO requisicao, CancellationToken ct) =>
        Responder(await contaService.AlterarSenha(usuarioAtual.Id, new AlterarSenha(requisicao.SenhaAtual, requisicao.NovaSenha), ct));
}
