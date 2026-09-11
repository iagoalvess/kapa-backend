using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Backend.Worker.Configuration;

/// <summary>
/// Log e telemetria do worker.
/// </summary>
/// <remarks>
/// Parecido com o da API, mas sem a instrumentação de ASP.NET Core — o worker não atende HTTP.
/// A duplicação de umas poucas dezenas de linhas é preferível a criar um projeto compartilhado
/// só para isto, que acabaria virando o depósito de tudo o que "os dois usam".
/// </remarks>
public static class ObservabilidadeConfig
{
    /// <summary>Variável que aponta o coletor OTLP. Vazia desliga a exportação.</summary>
    public const string VariavelDoColetor = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Configura log estruturado e telemetria.</summary>
    /// <param name="builder">Builder do host.</param>
    /// <param name="nomeDoServico">Nome que identifica este processo na telemetria.</param>
    public static IHostApplicationBuilder AddObservabilidade(this IHostApplicationBuilder builder, string nomeDoServico)
    {
        builder.Logging.ClearProviders();

        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.AddSimpleConsole(opcoes =>
            {
                opcoes.SingleLine = true;
                opcoes.TimestampFormat = "HH:mm:ss ";
            });
        }
        else
        {
            builder.Logging.AddJsonConsole(opcoes =>
            {
                opcoes.IncludeScopes = true;
                opcoes.UseUtcTimestamp = true;
            });
        }

        var coletor = builder.Configuration[VariavelDoColetor];

        builder
            .Services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso.AddService(nomeDoServico))
            .WithTracing(rastreamento =>
            {
                rastreamento.AddHttpClientInstrumentation().AddSource("Npgsql");

                if (!string.IsNullOrWhiteSpace(coletor))
                    rastreamento.AddOtlpExporter();
            })
            .WithMetrics(metricas =>
            {
                metricas.AddHttpClientInstrumentation().AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(coletor))
                    metricas.AddOtlpExporter();
            });

        return builder;
    }
}
