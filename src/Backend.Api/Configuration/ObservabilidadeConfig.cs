using Backend.Data;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Backend.Api.Configuration;

/// <summary>
/// Log, rastreamento distribuído, métricas e health checks.
/// </summary>
/// <remarks>
/// Sem Serilog. O que se queria dele — log estruturado em JSON no stdout — o
/// <c>AddJsonConsole</c> nativo entrega sem dependência, e log sozinho não responde "por que
/// esta requisição levou 4 segundos": isso é <i>trace</i>, que o OpenTelemetry dá.
/// <para>
/// A exportação é OTLP, aceita por Grafana/Tempo/Loki, Datadog, Jaeger, Honeycomb e New Relic —
/// trocar de backend de observabilidade é variável de ambiente, não refatoração.
/// </para>
/// </remarks>
public static class ObservabilidadeConfig
{
    /// <summary>Variável que aponta o coletor OTLP. Vazia desliga a exportação.</summary>
    public const string VariavelDoColetor = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Configura log, telemetria e health checks.</summary>
    /// <param name="builder">Builder da aplicação.</param>
    /// <param name="nomeDoServico">Nome que identifica este processo na telemetria.</param>
    public static IHostApplicationBuilder AddObservabilidade(this IHostApplicationBuilder builder, string nomeDoServico)
    {
        builder.AddLogEstruturado();
        builder.AddTelemetria(nomeDoServico);

        builder.Services.AddHealthChecks().AddNpgSql(builder.Configuration.GetConnectionString(DependenciasData.NomeDaConexao)!, name: "postgres");

        return builder;
    }

    /// <summary>
    /// Log legível em desenvolvimento, JSON de uma linha em produção.
    /// </summary>
    /// <remarks>
    /// JSON porque o agregador (Loki, Datadog, CloudWatch) indexa **campo**, não texto: com
    /// linha estruturada dá para filtrar por <c>UsuarioId</c>; com texto formatado sobra
    /// expressão regular frágil.
    /// </remarks>
    private static void AddLogEstruturado(this IHostApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();

        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.AddSimpleConsole(opcoes =>
            {
                opcoes.SingleLine = true;
                opcoes.TimestampFormat = "HH:mm:ss ";
            });

            return;
        }

        builder.Logging.AddJsonConsole(opcoes =>
        {
            opcoes.IncludeScopes = true;
            opcoes.UseUtcTimestamp = true;
        });
    }

    private static void AddTelemetria(this IHostApplicationBuilder builder, string nomeDoServico)
    {
        var coletor = builder.Configuration[VariavelDoColetor];

        builder
            .Services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso.AddService(nomeDoServico))
            .WithTracing(rastreamento =>
            {
                rastreamento
                    .AddAspNetCoreInstrumentation(opcoes =>
                        opcoes.Filter = contexto => !contexto.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
                    )
                    .AddHttpClientInstrumentation()
                    .AddSource("Npgsql");

                if (!string.IsNullOrWhiteSpace(coletor))
                    rastreamento.AddOtlpExporter();
            })
            .WithMetrics(metricas =>
            {
                metricas.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(coletor))
                    metricas.AddOtlpExporter();
            });
    }
}
