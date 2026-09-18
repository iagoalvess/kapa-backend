using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Leads;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Leads.Interfaces;
using Backend.Business.Leads.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Leads;

/// <summary>
/// Os contatos deixados no formulário da página institucional.
/// </summary>
/// <remarks>
/// Duas pontas opostas no mesmo recurso: o <c>POST</c> é anônimo, porque quem preenche ainda não
/// tem conta; o <c>GET</c> é do <c>Administrador</c> da plataforma, porque a lista é um cadastro de
/// dado pessoal de gente que nem cliente é. Nenhum papel de formatura chega aqui.
/// </remarks>
/// <param name="leadService">O formulário de contato.</param>
/// <param name="usuarioAtual">IP e navegador de quem envia, gravados como prova do consentimento.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/leads")]
public sealed class LeadController(ILeadService leadService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>
    /// Registra um contato. Responde 204 também quando o envio é descartado.
    /// </summary>
    /// <remarks>
    /// Honeypot preenchido e o mesmo e-mail dentro de 24 h respondem 204 sem gravar: quem enviou não
    /// tem nada a fazer com a diferença, e contá-la só serviria para o robô aprender.
    /// </remarks>
    /// <param name="requisicao">O que o formulário enviou.</param>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitConfig.Leads)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Registrar([FromBody] NovoLeadRequestDTO requisicao, CancellationToken ct)
    {
        var dados = new NovoLead(
            requisicao.Nome ?? string.Empty,
            requisicao.Email ?? string.Empty,
            requisicao.Telefone,
            requisicao.Instituicao ?? string.Empty,
            requisicao.Curso ?? string.Empty,
            requisicao.TamanhoDaTurma,
            requisicao.PrevisaoDeColacao,
            requisicao.Mensagem,
            requisicao.AceitaPrivacidade,
            requisicao.Origem,
            requisicao.Meio,
            requisicao.Campanha,
            requisicao.Sobrenome
        );

        var origem = new OrigemDoContato(usuarioAtual.EnderecoIp, usuarioAtual.UserAgent);

        return Responder(await leadService.Registrar(dados, origem, ct));
    }

    /// <summary>Os contatos recebidos, do mais recente para o mais antigo.</summary>
    /// <param name="paginacao">Página e tamanho; o teto é aplicado no servidor.</param>
    /// <param name="busca">Trecho de nome, e-mail, instituição ou curso.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.SomenteAdministrador)]
    [EnableRateLimiting(RateLimitConfig.Padrao)]
    [ProducesResponseType(typeof(PaginaDTO<LeadDTO>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Listar([FromQuery] PaginacaoRequestDTO paginacao, [FromQuery] string? busca, CancellationToken ct)
    {
        var resultado = await leadService.Listar(paginacao.ParaModelo(), busca, ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(lead => lead.Adapt<LeadDTO>())));
    }
}
