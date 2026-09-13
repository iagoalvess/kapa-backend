using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Auth;
using Backend.Api.DTOs.Formaturas;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Settings;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Backend.Api.Controllers.V1.Formaturas;

/// <summary>
/// Quais formaturas o usuário acessa, qual delas a sessão está enxergando, e a formatura em si.
/// </summary>
/// <remarks>
/// Listar, selecionar e criar exigem apenas autenticação, e não formatura selecionada: são o
/// caminho para ter uma. Exigir a claim ali deixaria o usuário preso num 403 sem saída.
/// <para>
/// As rotas <c>atual</c> não levam id: a formatura vem da claim do token, nunca de um valor que o
/// cliente escolhe.
/// </para>
/// </remarks>
/// <param name="formaturaService">Criação, seleção e ciclo de vida da formatura.</param>
/// <param name="usuarioAtual">Quem está fazendo a requisição.</param>
/// <param name="formaturaAtual">Formatura da sessão.</param>
/// <param name="cookieOptions">Configuração do cookie de sessão.</param>
/// <param name="jwtOptions">Configuração de JWT, que define a validade do cookie.</param>
/// <param name="configuration">Configuração da aplicação, de onde saem as origens permitidas.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/formaturas")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class FormaturaController(
    IFormaturaService formaturaService,
    IUsuarioAtual usuarioAtual,
    IFormaturaAtual formaturaAtual,
    IOptions<CookieDeSessaoSettings> cookieOptions,
    IOptions<JwtSettings> jwtOptions,
    IConfiguration configuration
) : MainController
{
    private string? IpDeOrigem => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>A política garante a claim; o <c>Guid.Empty</c> nunca chega a ser consultado.</summary>
    private Guid FormaturaId => formaturaAtual.Id ?? Guid.Empty;

    /// <summary>
    /// Cria a formatura em rascunho, com quem criou como Presidente.
    /// </summary>
    /// <remarks>
    /// Devolve o par de tokens já com <c>formatura_id</c> e <c>papel=Presidente</c>: economiza uma
    /// ida ao servidor e evita o estado esquisito de "criei a turma mas ainda não estou dentro dela".
    /// </remarks>
    /// <param name="requisicao">Dados cadastrais.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost]
    [Authorize(Policy = Politicas.Autenticado)]
    [RegistrarEvento("formatura.criada")]
    [ProducesResponseType(typeof(TokenResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Criar([FromBody] DadosDaFormaturaRequestDTO requisicao, CancellationToken ct)
    {
        var refreshAtual = Request.RefreshTokenRecebido(cookieOptions.Value, OrigensPermitidas, requisicao.RefreshToken);
        var resultado = await formaturaService.Criar(usuarioAtual.Id, requisicao.Adapt<DadosDaFormatura>(), refreshAtual, IpDeOrigem, ct);

        return Responder(RespostaDeSessao.Preparar(resultado, Response, cookieOptions.Value, jwtOptions.Value));
    }

    /// <summary>Detalhe da formatura selecionada, com o status. Leitura: vale em qualquer status.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("atual")]
    [Authorize(Policy = Politicas.MembroDaFormatura)]
    [ProducesResponseType(typeof(FormaturaDetalheDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterAtual(CancellationToken ct)
    {
        var resultado = await formaturaService.ObterAtual(FormaturaId, ct);

        return Responder(resultado.Map(detalhe => detalhe.Adapt<FormaturaDetalheDTO>()));
    }

    /// <summary>Edita os dados cadastrais. Recusa em formatura suspensa ou encerrada.</summary>
    /// <param name="requisicao">Dados novos.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPut("atual")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [RegistrarEvento("formatura.atualizada")]
    [ProducesResponseType(typeof(FormaturaDetalheDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Atualizar([FromBody] DadosDaFormaturaRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await formaturaService.Atualizar(FormaturaId, requisicao.Adapt<DadosDaFormatura>(), ct);

        return Responder(resultado.Map(detalhe => detalhe.Adapt<FormaturaDetalheDTO>()));
    }

    /// <summary>Encerra a formatura. Nada é apagado: leitura e exportação continuam.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("atual/encerrar")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [RegistrarEvento("formatura.encerrada")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Encerrar(CancellationToken ct)
    {
        var resultado = await formaturaService.Encerrar(FormaturaId, ct);

        return Responder(resultado);
    }

    /// <summary>
    /// Descarta um rascunho que nunca foi pago. A turma some da lista de todos os membros; o
    /// cliente renova a sessão em seguida para sair dela.
    /// </summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("atual/descartar")]
    [Authorize(Policy = Politicas.SomentePresidente)]
    [RegistrarEvento("formatura.descartada")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Descartar(CancellationToken ct)
    {
        var resultado = await formaturaService.Descartar(FormaturaId, ct);

        return Responder(resultado);
    }

    private string[] OrigensPermitidas => configuration.GetSection(ApiConfig.SecaoDeOrigens).Get<string[]>() ?? [];

    /// <summary>Lista as formaturas em que o usuário tem vínculo ativo.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("minhas")]
    [Authorize(Policy = Politicas.Autenticado)]
    [ProducesResponseType(typeof(IReadOnlyList<FormaturaDoUsuarioDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarMinhas(CancellationToken ct)
    {
        var resultado = await formaturaService.ListarMinhas(usuarioAtual.Id, ct);

        return Responder(resultado.Map(formaturas => formaturas.Adapt<List<FormaturaDoUsuarioDTO>>()));
    }

    /// <summary>
    /// Passa a sessão a enxergar a formatura informada.
    /// </summary>
    /// <remarks>
    /// Devolve o mesmo <c>ParDeTokens</c> do login — o front reaproveita o caminho de sessão
    /// que já existe, em vez de ganhar um segundo jeito de guardar credencial. O refresh token
    /// atual é rotacionado junto, para a troca não deixar uma sessão viva para trás.
    /// </remarks>
    /// <param name="id">Formatura pretendida.</param>
    /// <param name="requisicao">Refresh token atual, quando o modo cookie está desligado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpPost("{id:guid}/selecionar")]
    [Authorize(Policy = Politicas.Autenticado)]
    [RegistrarEvento("formatura.selecionada", CamposDaRota = ["id"])]
    [ProducesResponseType(typeof(TokenResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Selecionar(Guid id, [FromBody] RefreshRequestDTO? requisicao, CancellationToken ct)
    {
        var refreshAtual = Request.RefreshTokenRecebido(cookieOptions.Value, OrigensPermitidas, requisicao?.RefreshToken);
        var resultado = await formaturaService.Selecionar(usuarioAtual.Id, id, refreshAtual, IpDeOrigem, ct);

        return Responder(RespostaDeSessao.Preparar(resultado, Response, cookieOptions.Value, jwtOptions.Value));
    }
}
