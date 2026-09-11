using System.Text;
using System.Text.Json;
using Backend.Business.Auth.Services;
using Backend.Business.Auth.Settings;
using Backend.Business.Usuarios;
using Backend.Business.Usuarios.Models;
using Backend.Data.Context;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Api.Configuration;

/// <summary>
/// Identidade e autenticação por token.
/// </summary>
public static class AuthConfig
{
    /// <summary>Registra o ASP.NET Identity, o esquema JWT Bearer e as políticas.</summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    /// <exception cref="InvalidOperationException">Se a seção <c>Jwt</c> não estiver configurada.</exception>
    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt =
            configuration.GetSection(JwtSettings.Secao).Get<JwtSettings>()
            ?? throw new InvalidOperationException($"A seção '{JwtSettings.Secao}' não está configurada.");

        var conta = configuration.GetSection(ContaSettings.Secao).Get<ContaSettings>() ?? new ContaSettings();

        services.AddIdentityCoreConfigurado();
        services.AddValidadeDosLinks(conta);
        services.AddJwtBearerConfigurado(jwt);
        services.AddPoliticas();

        return services;
    }

    /// <summary>
    /// Define por quanto tempo valem os links de confirmação e de redefinição.
    /// </summary>
    /// <remarks>
    /// O padrão do Identity é **um dia**, longo demais para uma credencial que fica parada numa
    /// caixa de entrada. Vale para todos os tokens do provedor padrão, que é o usado por
    /// <c>GeneratePasswordResetTokenAsync</c> e <c>GenerateEmailConfirmationTokenAsync</c>.
    /// </remarks>
    private static IServiceCollection AddValidadeDosLinks(this IServiceCollection services, ContaSettings conta)
    {
        services.Configure<DataProtectionTokenProviderOptions>(opcoes => opcoes.TokenLifespan = TimeSpan.FromHours(conta.HorasDeValidadeDoLink));

        return services;
    }

    /// <summary>
    /// Registra o Identity em modo API.
    /// </summary>
    /// <remarks>
    /// <c>AddIdentityCore</c>, e não <c>AddIdentity</c>: o segundo registra os esquemas de
    /// autenticação por cookie, que disputam com o JWT Bearer e transformam um 401 de API em
    /// redirect 302 para uma tela de login que não existe.
    /// </remarks>
    private static IServiceCollection AddIdentityCoreConfigurado(this IServiceCollection services)
    {
        services
            .AddIdentityCore<Usuario>(opcoes =>
            {
                opcoes.User.RequireUniqueEmail = true;

                opcoes.Password.RequiredLength = 10;
                opcoes.Password.RequireDigit = true;
                opcoes.Password.RequireLowercase = true;
                opcoes.Password.RequireUppercase = true;
                opcoes.Password.RequireNonAlphanumeric = false;

                opcoes.Lockout.MaxFailedAccessAttempts = 5;
                opcoes.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                opcoes.Lockout.AllowedForNewUsers = true;
            })
            .AddErrorDescriber<MensagensDeIdentity>()
            .AddRoles<Perfil>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        return services;
    }

    private static IServiceCollection AddJwtBearerConfigurado(this IServiceCollection services, JwtSettings jwt)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opcoes =>
            {
                opcoes.MapInboundClaims = false;

                opcoes.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Emissor,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audiencia,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.ChaveSecreta)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Name,
                    RoleClaimType = TokenService.ClaimDePerfil,
                };

                opcoes.Events = new JwtBearerEvents
                {
                    OnChallenge = contexto =>
                    {
                        contexto.HandleResponse();
                        return EscreverProblema(
                            contexto.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "auth.nao_autenticado",
                            "Autenticação necessária."
                        );
                    },
                    OnForbidden = contexto =>
                        EscreverProblema(
                            contexto.HttpContext,
                            StatusCodes.Status403Forbidden,
                            "auth.sem_permissao",
                            "Você não tem permissão para esta operação."
                        ),
                };
            });

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, RespostaDeAutorizacao>();

        return services;
    }

    /// <summary>
    /// Escreve 401 e 403 no mesmo <c>ProblemDetails</c> que o resto da API usa.
    /// </summary>
    /// <remarks>
    /// Sem isto o ASP.NET responde 401 com corpo vazio, e o cliente precisa de um caminho de
    /// tratamento só para esses dois status. Um contrato de erro, não dois.
    /// </remarks>
    internal static Task EscreverProblema(HttpContext contexto, int status, string codigo, string titulo)
    {
        if (contexto.Response.HasStarted)
            return Task.CompletedTask;

        var problema = new ProblemDetails
        {
            Status = status,
            Title = titulo,
            Type = $"https://httpstatuses.io/{status}",
            Instance = $"{contexto.Request.Method} {contexto.Request.Path}",
        };

        problema.Extensions["codigo"] = codigo;
        problema.Extensions["traceId"] = contexto.TraceIdentifier;

        contexto.Response.StatusCode = status;
        contexto.Response.ContentType = "application/problem+json";

        return contexto.Response.WriteAsync(JsonSerializer.Serialize(problema, JsonSerializerOptions.Web));
    }
}
