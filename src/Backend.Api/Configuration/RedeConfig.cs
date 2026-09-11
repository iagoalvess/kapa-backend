using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
// Os dois namespaces declaram IPNetwork. O do ASP.NET está obsoleto desde o .NET 10;
// KnownIPNetworks espera o da BCL.
using IPNetwork = System.Net.IPNetwork;

namespace Backend.Api.Configuration;

/// <summary>
/// Confiança nos cabeçalhos de proxy reverso.
/// </summary>
/// <remarks>
/// Atrás de nginx, ingress ou load balancer, <c>RemoteIpAddress</c> é o endereço do <b>proxy</b>,
/// não o do cliente. Sem tratar isso, todo usuário anônimo cai na mesma partição de rate limit —
/// o limite vira global em vez de por cliente — e <c>CriadoPorIp</c> do refresh token grava
/// sempre o mesmo endereço, inútil para investigar um reúso de token.
/// <para>
/// <b>Confiar em <c>X-Forwarded-For</c> sem saber quem o enviou é pior que não tratar.</b> É um
/// cabeçalho que qualquer cliente escreve: se a aplicação aceitar o de qualquer origem, burlar o
/// rate limit passa a ser mandar um valor diferente a cada requisição. Por isso a lista de
/// proxies é explícita e, <b>vazia, o tratamento não é ligado</b> — o padrão seguro é ignorar o
/// cabeçalho, não adivinhar.
/// </para>
/// </remarks>
public static class RedeConfig
{
    /// <summary>Seção que lista os proxies em que a aplicação confia.</summary>
    public const string Secao = "Rede:ProxiesConfiaveis";

    /// <summary>
    /// Registra o tratamento de cabeçalhos de proxy, se houver proxy configurado.
    /// </summary>
    /// <remarks>
    /// Aceita endereço solto (<c>10.1.2.3</c>) ou faixa CIDR (<c>10.0.0.0/8</c>). As redes
    /// conhecidas padrão do ASP.NET — só <c>localhost</c> — são limpas: em container o proxy
    /// nunca é <c>localhost</c>, e deixá-las ligadas dá a falsa impressão de que algo foi
    /// configurado.
    /// </remarks>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    public static IServiceCollection AddProxyReverso(this IServiceCollection services, IConfiguration configuration)
    {
        var proxies = configuration.GetSection(Secao).Get<string[]>() ?? [];

        if (proxies.Length == 0)
            return services;

        services.Configure<ForwardedHeadersOptions>(opcoes =>
        {
            opcoes.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            opcoes.KnownProxies.Clear();
            opcoes.KnownIPNetworks.Clear();

            foreach (var entrada in proxies)
                Registrar(opcoes, entrada);
        });

        return services;
    }

    /// <summary>
    /// Aplica o tratamento, se configurado.
    /// </summary>
    /// <remarks>
    /// Precisa ser o **primeiro** middleware: tudo que lê o IP ou o esquema da requisição —
    /// rate limit, redirecionamento para HTTPS, log — precisa enxergar o valor já corrigido.
    /// </remarks>
    /// <param name="app">Aplicação web.</param>
    public static WebApplication UseProxyReverso(this WebApplication app)
    {
        if (app.Configuration.GetSection(Secao).Get<string[]>() is { Length: > 0 })
            app.UseForwardedHeaders();

        return app;
    }

    /// <summary>Registra uma entrada como faixa CIDR ou como endereço único.</summary>
    /// <param name="opcoes">Opções a preencher.</param>
    /// <param name="entrada">Endereço ou faixa, como veio da configuração.</param>
    /// <exception cref="InvalidOperationException">Se a entrada não for endereço nem faixa válida.</exception>
    private static void Registrar(ForwardedHeadersOptions opcoes, string entrada)
    {
        var valor = entrada.Trim();

        if (valor.Contains('/', StringComparison.Ordinal))
        {
            if (!IPNetwork.TryParse(valor, out var rede))
                throw new InvalidOperationException($"'{entrada}' não é uma faixa CIDR válida em {Secao}.");

            opcoes.KnownIPNetworks.Add(rede);

            return;
        }

        if (!IPAddress.TryParse(valor, out var endereco))
            throw new InvalidOperationException($"'{entrada}' não é um endereço IP válido em {Secao}.");

        opcoes.KnownProxies.Add(endereco);
    }
}
