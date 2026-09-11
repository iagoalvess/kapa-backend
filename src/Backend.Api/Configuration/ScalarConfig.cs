using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Backend.Api.Configuration;

/// <summary>
/// Documentação OpenAPI servida pelo Scalar.
/// </summary>
/// <remarks>
/// <c>AddOpenApi</c> é o gerador nativo do ASP.NET Core — o Swashbuckle saiu dos templates a
/// partir do .NET 9. O Scalar entra só como interface de leitura e teste.
/// <para>
/// Um documento por versão, listados em <see cref="Versoes"/>. O analisador do Asp.Versioning
/// (AV0029/AV0030) sugere <c>AddApiVersioning().AddOpenApi()</c> e
/// <c>MapOpenApi().WithDocumentPerVersion()</c>, que geram a lista sozinhos — mas essas APIs
/// ainda não existem nos pacotes estáveis 10.2.x. Quando existirem, troque por elas e apague
/// <see cref="Versoes"/>.
/// </para>
/// </remarks>
public static class ScalarConfig
{
    /// <summary>Chave de configuração que libera a documentação fora de desenvolvimento.</summary>
    public const string ChaveDeHabilitacao = "Documentacao:Habilitada";

    /// <summary>Versões da API que ganham documento. Acrescente aqui ao criar a V2.</summary>
    private static readonly string[] Versoes = ["v1"];

    /// <summary>Registra a geração dos documentos OpenAPI.</summary>
    /// <param name="services">Coleção de serviços.</param>
    public static IServiceCollection AddDocumentacao(this IServiceCollection services)
    {
        foreach (var versao in Versoes)
            services.AddOpenApi(versao, opcoes => opcoes.AddDocumentTransformer<SegurancaBearerTransformer>());

        return services;
    }

    /// <summary>
    /// Publica a documentação em <c>/scalar</c>.
    /// </summary>
    /// <remarks>
    /// Ligada por padrão só em desenvolvimento. Documentação aberta em produção entrega a
    /// superfície inteira da API — todas as rotas, todos os campos — para quem estiver olhando.
    /// Para liberar em homologação, use <c>Documentacao:Habilitada = true</c>.
    /// </remarks>
    /// <param name="app">Aplicação web.</param>
    public static WebApplication UseDocumentacao(this WebApplication app)
    {
        var habilitada = app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>(ChaveDeHabilitacao);

        if (!habilitada)
            return app;

        app.MapOpenApi().AllowAnonymous();

        app.MapScalarApiReference(opcoes =>
            {
                opcoes.Title = "Backend API";
                opcoes.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
            })
            .AllowAnonymous();

        return app;
    }
}

/// <summary>
/// Declara o esquema <c>Bearer</c> no documento OpenAPI.
/// </summary>
/// <remarks>
/// Sem isto o Scalar não mostra o campo de token, e testar qualquer endpoint protegido pela
/// interface fica impossível.
/// </remarks>
internal sealed class SegurancaBearerTransformer : IOpenApiDocumentTransformer
{
    /// <inheritdoc />
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(StringComparer.Ordinal);

        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Cole aqui o accessToken devolvido por POST /api/v1/auth/login.",
        };

        return Task.CompletedTask;
    }
}
