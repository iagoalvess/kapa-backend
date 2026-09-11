using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Api.Middleware;

/// <summary>
/// Último recurso: transforma exceção não tratada em <c>ProblemDetails</c>.
/// </summary>
/// <remarks>
/// Aqui só chega o que **ninguém previu** — banco fora do ar, bug, payload malformado. Falha de
/// negócio prevista nunca vira exceção: ela volta como <c>Result</c> e é traduzida pelo
/// <c>MainController</c>. Ver <c>docs/arquitetura.md</c>.
/// <para>
/// Implementa <see cref="IExceptionHandler"/>, a extensão nativa do ASP.NET Core, em vez de um
/// middleware próprio com <c>try/catch</c>: encaixa em <c>UseExceptionHandler</c>, participa do
/// pipeline de diagnóstico e não engole exceção lançada por outro middleware.
/// </para>
/// </remarks>
/// <param name="ambiente">Ambiente de execução, que decide se o detalhe técnico é exposto.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class GlobalExceptionHandler(IHostEnvironment ambiente, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Requisição {Metodo} {Caminho} cancelada pelo cliente.", httpContext.Request.Method, httpContext.Request.Path);
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var (status, titulo) = ClassificadorDeExcecao.Classificar(exception);

        logger.LogError(
            exception,
            "Falha não tratada em {Metodo} {Caminho} (status {Status}).",
            httpContext.Request.Method,
            httpContext.Request.Path,
            status
        );

        var problema = new ProblemDetails
        {
            Status = status,
            Title = titulo,
            Type = $"https://httpstatuses.io/{status}",
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
            Detail = ambiente.IsDevelopment() ? exception.ToString() : null,
        };

        problema.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problema, cancellationToken);

        return true;
    }
}
