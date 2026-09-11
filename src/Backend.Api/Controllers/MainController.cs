using Backend.Business.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Controllers;

/// <summary>
/// Base de todo controller da API.
/// </summary>
/// <remarks>
/// Faz uma coisa só: traduzir <see cref="Result"/> em resposta HTTP. Não tem acesso a
/// repositório, não tem <c>try/catch</c> e não decide regra — exceção que escape daqui é
/// tratada pelo <c>GlobalExceptionHandler</c>.
/// <para>
/// Autenticação é o **padrão**: a classe já vem com <c>[Authorize]</c>, e endpoint público
/// precisa declarar <c>[AllowAnonymous]</c> explicitamente. O contrário — proteger só o que
/// alguém lembrou de marcar — é como endpoint vaza.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public abstract class MainController : ControllerBase
{
    /// <summary>Responde 200 com o valor, ou o problema correspondente à falha.</summary>
    /// <typeparam name="T">Tipo do corpo de resposta.</typeparam>
    /// <param name="resultado">Resultado devolvido pelo service.</param>
    protected IActionResult Responder<T>(Result<T> resultado) => resultado.Sucesso ? Ok(resultado.Valor) : Problema(resultado);

    /// <summary>Responde 204 sem corpo, ou o problema correspondente à falha.</summary>
    /// <param name="resultado">Resultado devolvido pelo service.</param>
    protected IActionResult Responder(Result resultado) => resultado.Sucesso ? NoContent() : Problema(resultado);

    /// <summary>Responde 201 com o cabeçalho <c>Location</c>, ou o problema correspondente à falha.</summary>
    /// <typeparam name="T">Tipo do corpo de resposta.</typeparam>
    /// <param name="resultado">Resultado devolvido pelo service.</param>
    /// <param name="nomeDaRota">Nome da rota que localiza o recurso criado.</param>
    /// <param name="valoresDaRota">Valores para montar a rota.</param>
    protected IActionResult Criado<T>(Result<T> resultado, string nomeDaRota, object valoresDaRota) =>
        resultado.Sucesso ? CreatedAtRoute(nomeDaRota, valoresDaRota, resultado.Valor) : Problema(resultado);

    private ObjectResult Problema(Result resultado)
    {
        var erro = resultado.PrimeiroErro;
        var status = MapearStatus(erro.Tipo);

        ProblemDetails problema =
            erro.Tipo is ETipoErro.Validacao
                ? new ValidationProblemDetails(AgruparPorCampo(resultado.Erros)) { Title = "Os dados enviados são inválidos." }
                : new ProblemDetails { Title = erro.Mensagem };

        problema.Status = status;
        problema.Type = $"https://httpstatuses.io/{status}";
        problema.Instance = $"{Request.Method} {Request.Path}";
        problema.Extensions["codigo"] = erro.Codigo;
        problema.Extensions["traceId"] = HttpContext.TraceIdentifier;

        if (erro.Tipo is not ETipoErro.Validacao)
            problema.Detail = erro.Mensagem;

        return StatusCode(status, problema);
    }

    private static Dictionary<string, string[]> AgruparPorCampo(IReadOnlyList<Erro> erros) =>
        erros
            .GroupBy(e => e.Campo ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Select(e => e.Mensagem).ToArray(), StringComparer.Ordinal);

    private static int MapearStatus(ETipoErro tipo) =>
        tipo switch
        {
            ETipoErro.Validacao => StatusCodes.Status400BadRequest,
            ETipoErro.NaoAutenticado => StatusCodes.Status401Unauthorized,
            ETipoErro.Proibido => StatusCodes.Status403Forbidden,
            ETipoErro.NaoEncontrado => StatusCodes.Status404NotFound,
            ETipoErro.Conflito => StatusCodes.Status409Conflict,
            ETipoErro.Indisponivel => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };
}
