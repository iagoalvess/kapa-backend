using System.Globalization;
using System.Threading.RateLimiting;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    /// <summary>Limite estreito, por IP, para as rotas de conta que disparam e-mail ou conferem senha atual.</summary>
    public const string Autenticacao = "autenticacao";

    /// <summary>
    /// Limite para cadastro, login e confirmação de e-mail: balde por IP, com rajada.
    /// </summary>
    /// <remarks>
    /// Separado de <see cref="Autenticacao"/> pelo mesmo motivo de <see cref="Sessao"/>: a assembleia
    /// projeta o QR do convite e oitenta celulares no mesmo Wi-Fi — ou atrás do mesmo CGNAT da operadora —
    /// criam conta no mesmo minuto. Com a janela de 10 por minuto, setenta tomavam 429 antes de chegar
    /// ao aceite, que já tinha rajada própria (decisão de 23/09/2026).
    /// <para>
    /// A rajada não abre a força bruta de senha: ela conta por conta + origem em <c>TentativasDeSenha</c>.
    /// O que a rajada cede é um lote de contas novas de uma vez, e depois dela o balde volta ao ritmo de
    /// <c>EntradaPorMinuto</c>. <c>esqueci-senha</c> e <c>reenviar-confirmacao</c> ficam fora: rajada
    /// ali é e-mail em massa para a caixa de alguém.
    /// </para>
    /// </remarks>
    public const string Entrada = "entrada";

    /// <summary>
    /// Limite para renovar e encerrar a sessão: balde por IP, com rajada.
    /// </summary>
    /// <remarks>
    /// Separado de <see cref="Autenticacao"/> porque a renovação não adivinha nada — o cookie tem 512 bits
    /// —, e dividir o balde de 10 por minuto com o login fazia a assembleia no mesmo Wi-Fi tomar 429 ao
    /// reabrir o app: oitenta renovações no mesmo minuto, e ninguém mais conseguia nem entrar.
    /// </remarks>
    public const string Sessao = "sessao";

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

    /// <summary>
    /// Limite para abrir o convite da festa sem sessão — a página e o PDF (Sprint 21, decisão 11).
    /// </summary>
    /// <remarks>
    /// O mesmo desenho de <see cref="Convites"/>, e pelo motivo inverso do mesmo cenário: na porta do
    /// salão, dezenas de convidados abrem o convite no Wi-Fi da casa — um IP só — no mesmo minuto.
    /// A rajada cobre a porta; o ritmo sustentado é o que a varredura de códigos precisa, e é estreito.
    /// A portaria, que é gente logada, conta por usuário e não disputa a cota dos convidados.
    /// </remarks>
    public const string Ingresso = "ingresso";

    /// <summary>
    /// Limite estreito, por usuário, para pedir e conferir o código de seis dígitos da adesão.
    /// </summary>
    /// <remarks>
    /// Seis dígitos numa janela de poucos minutos é um espaço pequeno o bastante para força bruta
    /// valer a pena no limite padrão de 120 por minuto. Por <b>usuário</b>, e não por IP como a
    /// política de autenticação: a turma inteira adere do mesmo Wi-Fi da assembleia, e o código só
    /// serve para quem já entrou na conta — tentar o de outra pessoa exigiria o token dela.
    /// </remarks>
    public const string Codigo = "codigo";

    /// <summary>Limite estreito, por usuário, para conferir cupom e abrir checkout com ele (Sprint 51).</summary>
    /// <remarks>
    /// O cupom é código curto e humano — sem limite, o campo do checkout vira um adivinhador. Mesma cota padrão do
    /// <see cref="Codigo"/> (6 por minuto), em balde e chave de configuração próprios: errar o cupom não tranca o
    /// código da adesão.
    /// </remarks>
    public const string Cupom = "cupom";

    /// <summary>
    /// Limite para comprar na loja pública e mexer na compra pelo link: balde por IP, com rajada (Sprint 26,
    /// decisão 7).
    /// </summary>
    /// <remarks>
    /// Comprador anônimo sai por Wi-Fi de faculdade e CGNAT de operadora: uma janela fixa por IP barraria a
    /// turma inteira na abertura. A rajada cobre cem compradores do mesmo IP no mesmo minuto; quem segura o
    /// abuso é o limite por CPF, não o IP. Por IP mesmo com sessão: o comprador logado não ganha cota a mais.
    /// </remarks>
    public const string Loja = "loja";

    /// <summary>
    /// Limite para ler a vitrine da loja e a compra pelo link: balde folgado, separado do da compra.
    /// </summary>
    /// <remarks>
    /// A tela da loja relê o contador a cada poucos segundos. Dividir o balde com a compra faria quem só olha
    /// gastar a cota de quem vai comprar; aqui, o 429 da leitura só deixa o contador parado por um instante.
    /// </remarks>
    public const string Vitrine = "vitrine";

    /// <summary>Seção de configuração que ajusta os limites por ambiente.</summary>
    public const string Secao = "RateLimit";

    /// <summary>Código do 429, no mesmo vocabulário dos erros de negócio.</summary>
    private const string CodigoDoExcesso = "rate_limit.excedido";

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
        var porMinutoIngresso = configuration.GetValue($"{Secao}:IngressoPorMinuto", 30);
        var rajadaIngresso = configuration.GetValue($"{Secao}:IngressoRajada", 120);
        var porMinutoCodigo = configuration.GetValue($"{Secao}:CodigoPorMinuto", 6);
        var porMinutoCupom = configuration.GetValue($"{Secao}:CupomPorMinuto", 6);
        var porMinutoEntrada = configuration.GetValue($"{Secao}:EntradaPorMinuto", 10);
        var rajadaEntrada = configuration.GetValue($"{Secao}:EntradaRajada", 100);
        var simultaneasPorTurma = configuration.GetValue($"{Secao}:FilaPorTurmaSimultaneas", 4);
        var esperaPorTurma = configuration.GetValue($"{Secao}:FilaPorTurmaEspera", 500);
        var porMinutoSessao = configuration.GetValue($"{Secao}:SessaoPorMinuto", 60);
        var rajadaSessao = configuration.GetValue($"{Secao}:SessaoRajada", 200);
        var porMinutoLoja = configuration.GetValue($"{Secao}:LojaPorMinuto", 60);
        var rajadaLoja = configuration.GetValue($"{Secao}:LojaRajada", 300);
        var porMinutoVitrine = configuration.GetValue($"{Secao}:VitrinePorMinuto", 300);
        var rajadaVitrine = configuration.GetValue($"{Secao}:VitrineRajada", 600);

        services.AddSingleton(_ => new FilaPorTurma(simultaneasPorTurma, esperaPorTurma));
        services.Replace(ServiceDescriptor.Singleton<IFilaDaTurma>(sp => sp.GetRequiredService<FilaPorTurma>()));
        services
            .AddOptions<RateLimiterOptions>()
            .Configure<FilaPorTurma>(
                (opcoes, fila) =>
                    opcoes.GlobalLimiter = fila.ParaRequisicoes(contexto =>
                        contexto.GetEndpoint()?.Metadata.GetMetadata<FilaPorTurmaAttribute>() is null ? string.Empty : ChaveDaFila(contexto)
                    )
            );

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
                        type = DocDeErros.Para(contexto.HttpContext, CodigoDoExcesso),
                        status = StatusCodes.Status429TooManyRequests,
                        title = "Muitas requisições. Tente novamente em instantes.",
                        codigo = CodigoDoExcesso,
                        traceId = contexto.HttpContext.TraceIdentifier,
                    },
                    ct
                );
            };

            opcoes.AddPolicy(Padrao, contexto => LimitarPor(Identificar(contexto), porMinutoPadrao));

            opcoes.AddPolicy(Autenticacao, contexto => LimitarPor($"auth:{Ip(contexto)}", porMinutoAutenticacao));

            opcoes.AddPolicy(Entrada, contexto => BaldePor($"entrada:{Ip(contexto)}", rajadaEntrada, porMinutoEntrada));

            opcoes.AddPolicy(Sessao, contexto => BaldePor($"sessao:{Ip(contexto)}", rajadaSessao, porMinutoSessao));
            opcoes.AddPolicy(Codigo, contexto => LimitarPor($"codigo:{Identificar(contexto)}", porMinutoCodigo));
            opcoes.AddPolicy(Cupom, contexto => LimitarPor($"cupom:{Identificar(contexto)}", porMinutoCupom));

            opcoes.AddPolicy(Webhook, contexto => LimitarPor($"webhook:{Identificar(contexto)}", porMinutoWebhook));

            opcoes.AddPolicy(Convites, contexto => BaldePor($"convites:{Identificar(contexto)}", rajadaConvites, porMinutoConvites));

            opcoes.AddPolicy(Ingresso, contexto => BaldePor($"ingresso:{Identificar(contexto)}", rajadaIngresso, porMinutoIngresso));

            opcoes.AddPolicy(Loja, contexto => BaldePor($"loja:{Ip(contexto)}", rajadaLoja, porMinutoLoja));

            opcoes.AddPolicy(Vitrine, contexto => BaldePor($"vitrine:{Ip(contexto)}", rajadaVitrine, porMinutoVitrine));
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

    /// <summary>
    /// A fila de uma rota <see cref="FilaPorTurmaAttribute"/>: a turma do token, ou a própria rota para quem
    /// ainda não tem turma — o aceite do link, em que a rota carrega o convite.
    /// </summary>
    /// <remarks>
    /// É um limitador global, e não uma política: soma-se à política da rota em vez de substituí-la, e o
    /// limite por usuário continua valendo. A mesma <see cref="FilaPorTurma"/> serve à loja pública, que entra
    /// nela dentro do service, pelo <see cref="IFilaDaTurma"/>. Ordem de chegada (<c>OldestFirst</c>), porque é a promessa de
    /// quem abre vendas; <c>FilaPorTurmaSimultaneas</c> escritas por turma chegam ao banco, e a espera
    /// passa de <c>FilaPorTurmaEspera</c> para 429.
    /// </remarks>
    private static string ChaveDaFila(HttpContext contexto) =>
        contexto.User.FindFirst(TokenService.ClaimDeFormatura)?.Value is { } turma ? $"turma:{turma}" : $"rota:{contexto.Request.Path}";

    /// <summary>Balde de fichas: aceita uma rajada de <paramref name="rajada"/> e repõe <paramref name="porMinuto"/> por minuto.</summary>
    private static RateLimitPartition<string> BaldePor(string chave, int rajada, int porMinuto) =>
        RateLimitPartition.GetTokenBucketLimiter(
            chave,
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = rajada,
                TokensPerPeriod = porMinuto,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }
        );

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
