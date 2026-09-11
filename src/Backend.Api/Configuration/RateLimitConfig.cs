using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Backend.Api.Configuration;

/// <summary>
/// Limitação de taxa, usando o limitador nativo do ASP.NET Core.
/// </summary>
/// <remarks>
/// Nenhuma biblioteca: <c>System.Threading.RateLimiting</c> faz parte do runtime desde o .NET 7.
/// <para>
/// <b>Isto é por processo.</b> Com duas réplicas, o limite efetivo dobra. É proteção contra
/// abuso acidental e força bruta de senha, não contra DDoS distribuído — para isso o lugar
/// certo é a borda (nginx, Cloudflare, API gateway).
/// </para>
/// </remarks>
public static class RateLimitConfig
{
    /// <summary>Limite aplicado a todo endpoint que não pedir outro.</summary>
    public const string Padrao = "padrao";

    /// <summary>Limite estreito para login, registro e renovação de token.</summary>
    public const string Autenticacao = "autenticacao";

    /// <summary>Seção de configuração que ajusta os limites por ambiente.</summary>
    public const string Secao = "RateLimit";

    /// <summary>Registra as políticas de limitação.</summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    public static IServiceCollection AddRateLimit(this IServiceCollection services, IConfiguration configuration)
    {
        var porMinutoPadrao = configuration.GetValue($"{Secao}:PadraoPorMinuto", 120);
        var porMinutoAutenticacao = configuration.GetValue($"{Secao}:AutenticacaoPorMinuto", 10);

        services.AddRateLimiter(opcoes =>
        {
            opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            opcoes.OnRejected = async (contexto, ct) =>
            {
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                    contexto.HttpContext.Response.Headers.RetryAfter = ((int)espera.TotalSeconds).ToString(CultureInfo.InvariantCulture);

                await contexto.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        status = StatusCodes.Status429TooManyRequests,
                        title = "Muitas requisições. Tente novamente em instantes.",
                        codigo = "rate_limit.excedido",
                        traceId = contexto.HttpContext.TraceIdentifier,
                    },
                    ct
                );
            };

            opcoes.AddPolicy(Padrao, contexto => LimitarPor(Identificar(contexto), porMinutoPadrao));

            opcoes.AddPolicy(Autenticacao, contexto => LimitarPor($"auth:{Identificar(contexto)}", porMinutoAutenticacao));
        });

        return services;
    }

    /// <summary>
    /// Identifica quem está chamando: o usuário autenticado, ou o IP quando anônimo.
    /// </summary>
    /// <remarks>
    /// Usuário antes de IP porque um escritório inteiro sai pelo mesmo IP — limitar por IP puro
    /// faria um usuário barulhento bloquear os colegas.
    /// <para>
    /// A chave é o <c>sub</c>, e não <c>User.Identity.Name</c>: com
    /// <c>NameClaimType = JwtRegisteredClaimNames.Name</c>, <c>Name</c> é o nome de exibição que
    /// o próprio usuário escolhe. Dois "João Silva" dividiriam a mesma cota — e quem quisesse
    /// derrubar alguém bastaria se cadastrar com o nome da vítima.
    /// </para>
    /// </remarks>
    private static string Identificar(HttpContext contexto) =>
        contexto.User.Identity?.IsAuthenticated == true
            ? contexto.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "autenticado"
            : contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

    private static RateLimitPartition<string> LimitarPor(string chave, int permissaoPorMinuto) =>
        RateLimitPartition.GetFixedWindowLimiter(
            chave,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permissaoPorMinuto,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }
        );
}
