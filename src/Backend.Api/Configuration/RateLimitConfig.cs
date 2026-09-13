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

    /// <summary>Limite folgado, por IP, para webhook de provedor: apertado demais faz o provedor desistir de reentregar.</summary>
    public const string Webhook = "webhook";

    /// <summary>
    /// Limite para consultar e aceitar convite pelo token: rajada folgada, ritmo sustentado estreito.
    /// </summary>
    /// <remarks>
    /// É o único lugar onde adivinhar um valor dá acesso a uma formatura. Os 256 bits do token já
    /// tornam a adivinhação inviável; o limite tira do endpoint o papel de alvo de varredura.
    /// <para>
    /// Balde de fichas, e não janela fixa, porque os dois usos têm formas opostas: a assembleia
    /// projeta o QR e oitenta celulares no mesmo Wi-Fi (mesmo IP) abrem o link no mesmo minuto — uma
    /// rajada que acaba; a varredura precisa de ritmo contínuo. A rajada cobre a turma, e depois
    /// dela o balde repõe <c>ConvitesPorMinuto</c> — em fluxo contínuo, uma ficha a cada três
    /// segundos com o padrão de 20.
    /// </para>
    /// </remarks>
    public const string Convites = "convites";

    /// <summary>Seção de configuração que ajusta os limites por ambiente.</summary>
    public const string Secao = "RateLimit";

    /// <summary>Registra as políticas de limitação.</summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    public static IServiceCollection AddRateLimit(this IServiceCollection services, IConfiguration configuration)
    {
        var porMinutoPadrao = configuration.GetValue($"{Secao}:PadraoPorMinuto", 120);
        var porMinutoAutenticacao = configuration.GetValue($"{Secao}:AutenticacaoPorMinuto", 10);
        var porMinutoWebhook = configuration.GetValue($"{Secao}:WebhookPorMinuto", 600);
        var porMinutoConvites = configuration.GetValue($"{Secao}:ConvitesPorMinuto", 20);
        var rajadaConvites = configuration.GetValue($"{Secao}:ConvitesRajada", 150);

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

            opcoes.AddPolicy(Autenticacao, contexto => LimitarPor($"auth:{Ip(contexto)}", porMinutoAutenticacao));

            opcoes.AddPolicy(Webhook, contexto => LimitarPor($"webhook:{Identificar(contexto)}", porMinutoWebhook));

            opcoes.AddPolicy(
                Convites,
                contexto =>
                    RateLimitPartition.GetTokenBucketLimiter(
                        $"convites:{Identificar(contexto)}",
                        _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = rajadaConvites,
                            TokensPerPeriod = porMinutoConvites,
                            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        }
                    )
            );
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
        contexto.User.Identity?.IsAuthenticated == true ? contexto.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ?? "autenticado" : Ip(contexto);

    /// <summary>
    /// IP de origem, ignorando quem está autenticado. É a chave da política de autenticação.
    /// </summary>
    /// <remarks>
    /// Login, cadastro e renovação são anônimos, mas o <c>UseAuthentication</c> roda antes do
    /// limitador: quem manda um Bearer válido junto cairia na cota do próprio <c>sub</c>. Com N
    /// contas, seriam N × 10 tentativas de senha por minuto a partir de uma máquina só.
    /// </remarks>
    /// <param name="contexto">Requisição atual.</param>
    private static string Ip(HttpContext contexto) => contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

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
