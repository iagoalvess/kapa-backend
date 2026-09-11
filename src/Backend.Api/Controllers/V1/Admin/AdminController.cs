using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Admin;
using Backend.Business.Abstractions;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Usuarios.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Admin;

/// <summary>
/// Painel administrativo.
/// </summary>
/// <remarks>
/// Tudo aqui exige <see cref="Politicas.SomenteAdministrador"/>, declarado na classe: um
/// endpoint novo neste controller já nasce restrito, sem depender de alguém lembrar do atributo.
/// <para>
/// A gestão de usuários — listar, editar, ativar, trocar perfil — fica em
/// <c>/api/v1/usuarios</c> e não é duplicada aqui; o front do painel consome os dois.
/// </para>
/// </remarks>
/// <param name="adminService">Operações do painel.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin")]
[Authorize(Policy = Politicas.SomenteAdministrador)]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class AdminController(IAdminService adminService) : MainController
{
    /// <summary>Números que alimentam a tela inicial do painel.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    [HttpGet("resumo")]
    [ProducesResponseType(typeof(ResumoAdminDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObterResumo(CancellationToken ct)
    {
        var resultado = await adminService.ObterResumo(ct);

        return Responder(resultado.Map(resumo => resumo.Adapt<ResumoAdminDTO>()));
    }

    /// <summary>
    /// Lista os perfis de acesso que o sistema reconhece.
    /// </summary>
    /// <remarks>
    /// O front usa isto para montar o seletor de perfis. Sem o endpoint, a lista acaba
    /// duplicada em código no front e sai de sincronia na primeira vez que um perfil é criado.
    /// </remarks>
    [HttpGet("perfis")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public IActionResult ListarPerfis() => Responder(Result.Ok(PerfisPadrao.Todos));
}
