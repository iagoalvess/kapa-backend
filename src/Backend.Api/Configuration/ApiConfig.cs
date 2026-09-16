using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Backend.Api.Analytics;
using Backend.Api.Extensions;
using Backend.Api.Middleware;
using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Settings;
using Backend.Business.Eventos.Interfaces;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    /// <remarks>
    /// Sem <c>SuppressImplicitRequired…</c>, todo <c>string</c> não-anulável de DTO vira
    /// <c>[Required]</c> implícito: corpo sem o campo leva um 400 do framework, em inglês e sem
    /// <c>codigo</c>, antes do validador. Validação vive uma vez — no validador do <c>Business</c>.
    /// <para>
    /// <b>O corpo JSON é snake_case</b>, na ida e na volta. <c>DictionaryKeyPolicy</c> vai junto
    /// porque as chaves que o cliente lê não são só propriedades: <c>errors</c> do
    /// <c>ValidationProblemDetails</c> e as extensões do <c>ProblemDetails</c> são dicionários, e
    /// sem ela o erro de validação sairia com o campo em camelCase no meio de um corpo snake_case.
    /// </para>
    /// <para>
    /// <b>Valor de enum não acompanha.</b> Continua <c>Pendente</c>, <c>Buffet</c>, <c>Turma</c>:
    /// vários deles são persistidos como texto (<c>HasConversion&lt;string&gt;</c>), e mudar a
    /// serialização deixaria o JSON discordando da coluna. Nome de campo é contrato de transporte;
    /// valor de enum é dado.
    /// </para>
    /// <para>
    /// <b>Nulo é escrito.</b> Omitir o campo nulo faz o cliente tipado receber <c>undefined</c>, que
    /// é indistinguível de "a API não manda mais esse campo" — e o mock de teste com <c>null</c>
    /// esconde a diferença até alguém tropeçar nela em produção. Campo declarado no contrato
    /// aparece sempre; ausente quer dizer removido.
    /// </para>
    /// </remarks>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddControllers(opcoes =>
            {
                opcoes.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
                opcoes.ValueProviderFactories.Add(new ValoresEmSnakeCase());
            })
            .AddJsonOptions(opcoes =>
            {
                opcoes.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
                opcoes.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
                opcoes.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        // O MVC e o `WriteAsJsonAsync` do `HttpResponse` leem opções diferentes: a primeira serve
        // aos controllers, a segunda ao que escreve na resposta por fora deles — o limitador de
        // taxa, o GlobalExceptionHandler e o 401 do JWT. Sem as duas, metade da API fala snake_case.
        services.ConfigureHttpJsonOptions(opcoes =>
        {
            opcoes.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            opcoes.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
            opcoes.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<IUsuarioAtual, UsuarioAtual>();
        services.Replace(ServiceDescriptor.Scoped<IFormaturaAtual, FormaturaAtual>());

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
    /// <remarks>
    /// O limitador vem <b>depois</b> da autenticação: antes dela o <c>User</c> ainda é anônimo, e
    /// toda partição caía no IP — uma turma inteira no Wi-Fi da faculdade dividindo a mesma cota.
    /// E vem antes da autorização, para a requisição recusada não pagar a consulta de vínculo.
    /// </remarks>
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
        app.UseAuthentication();
        app.UseRateLimiter();
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
