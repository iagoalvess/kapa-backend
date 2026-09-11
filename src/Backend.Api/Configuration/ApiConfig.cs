using System.Text.Json.Serialization;
using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Extensions;
using Backend.Api.Middleware;
using Backend.Business.Arquivos.Settings;
using Backend.Business.Eventos.Interfaces;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Backend.Api.Configuration;

/// <summary>
/// Controllers, versionamento, serialização, CORS e tratamento de erro.
/// </summary>
public static class ApiConfig
{
    /// <summary>Nome da seção que lista as origens liberadas no CORS.</summary>
    public const string SecaoDeOrigens = "Cors:Origens";

    private const string PoliticaDeCors = "PadraoDaAplicacao";

    /// <summary>Registra os serviços da borda HTTP.</summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddControllers()
            .AddJsonOptions(opcoes =>
            {
                opcoes.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                opcoes.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<IUsuarioAtual, UsuarioAtual>();

        services.AddEventos();
        services.AddLimitesDeUpload(configuration);
        services.AddProxyReverso(configuration);
        services.AddCookieDeSessao(configuration);

        services.AddVersionamento();
        services.AddMapeamento();
        services.AddCorsConfigurado(configuration);

        return services;
    }

    /// <summary>Monta o pipeline de requisição, na ordem em que ele precisa rodar.</summary>
    /// <param name="app">Aplicação web.</param>
    public static WebApplication UseApi(this WebApplication app)
    {
        app.UseProxyReverso();
        app.UseExceptionHandler();

        if (!app.Environment.IsDevelopment())
            app.UseHsts();

        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseCors(PoliticaDeCors);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }

    /// <summary>
    /// Captura de eventos de uso.
    /// </summary>
    /// <remarks>
    /// A fila é singleton porque é memória do processo; a descarga é um serviço hospedado que
    /// vive junto da API, e não no worker, pelo mesmo motivo.
    /// </remarks>
    private static IServiceCollection AddEventos(this IServiceCollection services)
    {
        services.AddSingleton<FilaDeEventos>();
        services.AddSingleton<IRegistradorDeEventos>(sp => sp.GetRequiredService<FilaDeEventos>());
        services.AddHostedService<FlushDeEventosService>();

        return services;
    }

    /// <summary>
    /// Alinha os limites de corpo da requisição ao tamanho máximo de arquivo configurado.
    /// </summary>
    /// <remarks>
    /// São dois tetos independentes, e o menor vence: o do Kestrel (30 MB por padrão) e o do
    /// leitor de multipart (128 MB por padrão). Sem alinhá-los ao limite da aplicação, um envio
    /// dentro do permitido seria cortado pelo servidor antes de chegar ao validador — e a resposta
    /// seria um erro de protocolo, não a mensagem explicando o limite.
    /// <para>
    /// A folga de 1 MB cobre os cabeçalhos e as fronteiras do multipart, que acompanham o arquivo
    /// no mesmo corpo.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddLimitesDeUpload(this IServiceCollection services, IConfiguration configuration)
    {
        var armazenamento = configuration.GetSection(ArmazenamentoSettings.Secao).Get<ArmazenamentoSettings>() ?? new ArmazenamentoSettings();

        var limite = armazenamento.TamanhoMaximoEmBytes + (1024 * 1024);

        services.Configure<FormOptions>(opcoes => opcoes.MultipartBodyLengthLimit = limite);
        services.Configure<KestrelServerOptions>(opcoes => opcoes.Limits.MaxRequestBodySize = limite);

        return services;
    }

    /// <summary>
    /// Versionamento pelo segmento da URL.
    /// </summary>
    /// <remarks>
    /// A versão vai no caminho (<c>/api/v1/...</c>) e não em cabeçalho: é visível no log, no
    /// navegador e no <c>curl</c> que alguém cola num chamado, sem precisar reproduzir o
    /// cabeçalho para saber qual versão o cliente chamou.
    /// <para>
    /// <c>AssumeDefaultVersionWhenUnspecified</c> fica desligado de propósito: ele existe para
    /// APIs antigas que ganharam versionamento depois. Como aqui toda rota já nasce versionada,
    /// ligá-lo só criaria um segundo endereço não versionado para cada endpoint.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddVersionamento(this IServiceCollection services)
    {
        services
            .AddApiVersioning(opcoes =>
            {
                opcoes.DefaultApiVersion = new ApiVersion(1, 0);
                opcoes.ReportApiVersions = true;
                opcoes.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddMvc()
            .AddApiExplorer(opcoes =>
            {
                opcoes.GroupNameFormat = "'v'VVV";
                opcoes.SubstituteApiVersionInUrl = true;
            });

        return services;
    }

    /// <summary>
    /// Mapeamento entre domínio e DTO.
    /// </summary>
    /// <remarks>
    /// <c>RequireDestinationMemberSource</c> faz o mapeamento **falhar** quando um campo do
    /// destino não tem origem, em vez de preenchê-lo com <c>null</c>. Sem isso, renomear uma
    /// propriedade no domínio compila, passa no teste e chega ao front como campo vazio.
    /// </remarks>
    private static IServiceCollection AddMapeamento(this IServiceCollection services)
    {
        var configuracao = TypeAdapterConfig.GlobalSettings;
        configuracao.Default.RequireDestinationMemberSource(true);
        configuracao.Scan(typeof(ApiConfig).Assembly);

        services.AddSingleton(configuracao);
        services.AddScoped<IMapper, ServiceMapper>();

        return services;
    }

    /// <summary>
    /// CORS a partir da configuração.
    /// </summary>
    /// <remarks>
    /// Fora de desenvolvimento, sem <c>Cors:Origens</c> configurado **nenhuma** origem é
    /// liberada. A alternativa preguiçosa — cair para <c>AllowAnyOrigin</c> — é como uma API
    /// interna vira pública sem ninguém perceber.
    /// </remarks>
    private static IServiceCollection AddCorsConfigurado(this IServiceCollection services, IConfiguration configuration)
    {
        var origens = configuration.GetSection(SecaoDeOrigens).Get<string[]>() ?? [];

        services.AddCors(opcoes =>
            opcoes.AddPolicy(
                PoliticaDeCors,
                politica =>
                {
                    if (origens.Length == 0)
                        politica.WithOrigins().AllowAnyHeader().AllowAnyMethod();
                    else
                        politica.WithOrigins(origens).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
                }
            )
        );

        return services;
    }
}
