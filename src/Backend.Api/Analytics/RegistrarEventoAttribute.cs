using System.Text.Json;
using Backend.Api.Extensions;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Backend.Api.Analytics;

/// <summary>
/// Registra um evento de uso quando a ação termina com sucesso.
/// </summary>
/// <remarks>
/// Marque só o que é **evento de negócio** — "produto criado", "relatório exportado". Tráfego
/// HTTP bruto (quem chamou o quê, quanto demorou, qual status) já está nos traces do
/// OpenTelemetry e nos logs; duplicar isso numa tabela só produz custo de disco.
/// <example>
/// <code>
/// [RegistrarEvento("produto.criado")]
/// [HttpPost]
/// public async Task&lt;IActionResult&gt; Criar(...)
/// </code>
/// </example>
/// <para>
/// Só grava em resposta 2xx: uma tentativa recusada por validação não é um produto criado, e
/// contá-la inflaria a métrica justamente nos períodos em que algo estava quebrado.
/// </para>
/// </remarks>
/// <param name="nome">Nome do evento, no formato <c>recurso.acao</c>.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RegistrarEventoAttribute(string nome) : Attribute, IAsyncActionFilter
{
    /// <summary>Nome do evento registrado por esta ação.</summary>
    public string Nome { get; } = nome;

    /// <summary>Campos da rota a incluir em <c>Dados</c> — por exemplo o id do recurso afetado.</summary>
    public string[] CamposDaRota { get; init; } = [];

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executado = await next();

        if (!FoiSucesso(executado))
            return;

        var registrador = context.HttpContext.RequestServices.GetService<IRegistradorDeEventos>();
        if (registrador is null)
            return;

        var usuario = context.HttpContext.RequestServices.GetService<IUsuarioAtual>();

        registrador.Registrar(
            new Evento
            {
                Nome = Nome,
                UsuarioId = usuario?.Autenticado == true ? usuario.Id : null,
                OcorridoEm = DateTime.UtcNow,
                Rota = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}",
                Dados = MontarDados(context),
            }
        );
    }

    private static bool FoiSucesso(ActionExecutedContext executado)
    {
        if (executado.Exception is not null)
            return false;

        var status = (executado.Result as IStatusCodeActionResult)?.StatusCode ?? executado.HttpContext.Response.StatusCode;

        return status is >= 200 and < 300;
    }

    private string? MontarDados(ActionExecutingContext context)
    {
        if (CamposDaRota.Length == 0)
            return null;

        var dados = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var campo in CamposDaRota)
        {
            if (context.RouteData.Values.TryGetValue(campo, out var valor))
                dados[campo] = valor?.ToString();
        }

        return dados.Count == 0 ? null : JsonSerializer.Serialize(dados, JsonSerializerOptions.Web);
    }
}
