using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Usuarios;
using Backend.Api.Extensions;
using Backend.Business.Abstractions;
using Backend.Business.Usuarios.Interfaces;
using Backend.Business.Usuarios.Models;
using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Usuarios;

/// <summary>
/// Consulta e gestão de usuários, só para o administrador.
/// </summary>
/// <remarks>
/// O próprio cadastro do membro é lido e alterado por <c>/formandos/eu</c>, que nunca aceita um id
/// vindo do cliente.
/// </remarks>
/// <param name="usuarioService">Regras de gestão de usuários.</param>
/// <param name="usuarioAtual">Identidade da requisição.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/usuarios")]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class UsuarioController(IUsuarioService usuarioService, IUsuarioAtual usuarioAtual) : MainController
{
    /// <summary>Lista usuários paginados.</summary>
    /// <param name="paginacao">Página e tamanho.</param>
    /// <param name="busca">Termo livre aplicado a nome e e-mail.</param>
    [HttpGet]
    [Authorize(Policy = Politicas.SomenteAdministrador)]
    [ProducesResponseType(typeof(PaginaDTO<UsuarioResumoDTO>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] PaginacaoRequestDTO paginacao, [FromQuery] string? busca, CancellationToken ct)
    {
        var resultado = await usuarioService.Listar(paginacao.ParaModelo(), busca, ct);

        return Responder(resultado.Map(pagina => pagina.ParaDTO(usuario => usuario.Adapt<UsuarioResumoDTO>())));
    }

    /// <summary>Obtém um usuário pelo identificador.</summary>
    /// <param name="id">Identificador do usuário.</param>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Politicas.SomenteAdministrador)]
    [ProducesResponseType(typeof(UsuarioDetalheDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorId(Guid id, CancellationToken ct)
    {
        var resultado = await usuarioService.ObterPorId(id, ct);

        return Responder(resultado.Map(usuario => usuario.Adapt<UsuarioDetalheDTO>()));
    }

    /// <summary>Altera os dados de um usuário.</summary>
    /// <param name="id">Identificador do usuário.</param>
    /// <param name="requisicao">Novos valores.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Politicas.SomenteAdministrador)]
    [ProducesResponseType(typeof(UsuarioDetalheDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Atualizar(Guid id, [FromBody] AtualizarUsuarioRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await usuarioService.Atualizar(id, new AtualizarUsuario(requisicao.Nome), ct);

        return Responder(resultado.Map(usuario => usuario.Adapt<UsuarioDetalheDTO>()));
    }

    /// <summary>Ativa ou desativa o acesso de um usuário.</summary>
    /// <param name="id">Identificador do usuário.</param>
    /// <param name="requisicao">Novo estado de ativação.</param>
    [RegistrarEvento("usuario.ativacao_alterada", CamposDaRota = ["id"])]
    [HttpPut("{id:guid}/ativacao")]
    [Authorize(Policy = Politicas.SomenteAdministrador)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AlterarAtivacao(Guid id, [FromBody] AlterarAtivacaoRequestDTO requisicao, CancellationToken ct) =>
        Responder(await usuarioService.AlterarAtivacao(id, requisicao.Ativo, usuarioAtual.Id, ct));

    /// <summary>Substitui os perfis de acesso de um usuário.</summary>
    /// <param name="id">Identificador do usuário.</param>
    /// <param name="requisicao">Conjunto completo de perfis.</param>
    [RegistrarEvento("usuario.perfis_alterados", CamposDaRota = ["id"])]
    [HttpPut("{id:guid}/perfis")]
    [Authorize(Policy = Politicas.SomenteAdministrador)]
    [ProducesResponseType(typeof(UsuarioDetalheDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AlterarPerfis(Guid id, [FromBody] AlterarPerfisRequestDTO requisicao, CancellationToken ct)
    {
        var resultado = await usuarioService.AlterarPerfis(id, new AlterarPerfis(requisicao.Perfis), usuarioAtual.Id, ct);

        return Responder(resultado.Map(usuario => usuario.Adapt<UsuarioDetalheDTO>()));
    }
}
