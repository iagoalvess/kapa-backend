using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Auth;
using Backend.Api.DTOs.Legal;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Legal.Interfaces;
using Backend.Business.Legal.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Legal;

/// <summary>
/// Termos de Uso, Política de Privacidade e o consentimento a eles.
/// </summary>
/// <remarks>
/// A leitura é anônima: os documentos precisam abrir antes do cadastro e ter link permanente por
/// versão. Versão nova não bloqueia a API — quem aceitou a anterior continua usando o sistema, e
/// o front abre a tela de re-aceite a partir de <c>meus-aceites</c>.
/// </remarks>
/// <param name="legalService">Documentos e consentimento.</param>
/// <param name="usuarioAtual">Quem está fazendo a requisição.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/legal")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class LegalController(ILegalService legalService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>A versão vigente de cada documento, com o texto em markdown.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("vigentes")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentoLegalDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarVigentes(CancellationToken ct)
    {
        var resultado = await legalService.ListarVigentes(ct);

        return Responder(resultado.Map(documentos => documentos.Adapt<List<DocumentoLegalDTO>>()));
    }

    /// <summary>Uma versão específica de um documento, vigente ou não.</summary>
    /// <param name="tipo"><c>TermosDeUso</c> ou <c>PoliticaDePrivacidade</c>, em qualquer caixa.</param>
    /// <param name="versao">Rótulo da versão.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("{tipo}/{versao}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(DocumentoLegalDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterVersao(string tipo, string versao, CancellationToken ct)
    {
        var resultado = await legalService.ObterVersao(tipo, versao, ct);

        return Responder(resultado.Map(documento => documento.Adapt<DocumentoLegalDTO>()));
    }

    /// <summary>Registra o aceite de uma ou mais versões vigentes.</summary>
    /// <param name="requisicao">Versões aceitas.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("aceites")]
    [Authorize(Policy = Politicas.Autenticado)]
    [RegistrarEvento("legal.aceite_registrado")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegistrarAceites([FromBody] RegistrarAceitesRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await legalService.RegistrarAceites(
            usuarioAtual.Id,
            [.. (requisicao.Aceites ?? []).OfType<AceiteDeDocumentoDTO>().Select(aceite => new AceiteDeDocumento(aceite.Tipo, aceite.Versao))],
            new OrigemDoAceite(usuarioAtual.EnderecoIp, usuarioAtual.UserAgent),
            ct
        );

        return Responder(resultado);
    }

    /// <summary>Histórico de consentimento do usuário e as versões vigentes que ele ainda não aceitou.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("meus-aceites")]
    [Authorize(Policy = Politicas.Autenticado)]
    [ProducesResponseType(typeof(MeusAceitesDTO), StatusCodes.Status200OK)]
    public async Task<IActionResult> ObterMeusAceites(CancellationToken ct)
    {
        var resultado = await legalService.ObterMeusAceites(usuarioAtual.Id, ct);

        return Responder(resultado.Map(aceites => aceites.Adapt<MeusAceitesDTO>()));
    }
}
