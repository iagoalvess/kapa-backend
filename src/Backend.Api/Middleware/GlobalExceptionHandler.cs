using Backend.Api.Configuration;
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
/// <para>
/// O detalhe técnico, só em Development, vai na extensão <c>excecao</c> — nunca em <c>detail</c>.
/// <c>detail</c> é a mensagem para o usuário (o <c>MainController</c> a preenche nas falhas de
/// negócio) e o front a exibe; o stack trace ali acabava na tela.
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
            Type = DocDeErros.Para(httpContext, DocDeErros.Inesperado),
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
        };

        problema.Extensions["codigo"] = DocDeErros.Inesperado;
        problema.Extensions["trace_id"] = httpContext.TraceIdentifier;

        if (ambiente.IsDevelopment())
            problema.Extensions["excecao"] = exception.ToString();

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problema, cancellationToken);

        return true;
    }
}
