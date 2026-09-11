using Backend.Api.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Backend.UnitTests.Api;

/// <summary>
/// Cobre a confiança em proxy reverso.
/// </summary>
/// <remarks>
/// O caso que mais importa é o <b>padrão</b>: sem configuração, <c>X-Forwarded-For</c> precisa
/// continuar ignorado. Ligar o tratamento sem saber quem é o proxy transforma o cabeçalho — que
/// qualquer cliente escreve — em um jeito de trocar de identidade a cada requisição e escapar
/// do rate limit.
/// <para>
/// <c>ForwardedHeadersOptions</c> mora em <c>Microsoft.AspNetCore.Builder</c>, e não no
/// <c>HttpOverrides</c> do enum que a acompanha. No <c>Backend.Api</c> isso passa despercebido
/// porque o SDK Web importa <c>Builder</c> implicitamente; aqui o <c>using</c> é explícito.
/// </para>
/// </remarks>
public sealed class RedeConfigTests
{
    private static ForwardedHeadersOptions Configurar(params string[] proxies)
    {
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(proxies.Select((valor, indice) => new KeyValuePair<string, string?>($"{RedeConfig.Secao}:{indice}", valor)))
            .Build();

        var servicos = new ServiceCollection().AddOptions().AddProxyReverso(configuracao).BuildServiceProvider();

        return servicos.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }

    /// <summary>
    /// Sem proxy configurado, o cabeçalho continua ignorado.
    /// </summary>
    /// <remarks>
    /// A asserção é sobre <c>ForwardedHeaders</c>, e não sobre <c>KnownProxies</c>: o padrão do
    /// ASP.NET já traz o loopback na lista, e é <c>ForwardedHeaders.None</c> que determina que
    /// nenhum cabeçalho será lido.
    /// </remarks>
    [Fact]
    public void Sem_proxy_configurado_o_cabecalho_e_ignorado()
    {
        Configurar().ForwardedHeaders.ShouldBe(ForwardedHeaders.None);
    }

    [Fact]
    public void Endereco_solto_vira_proxy_conhecido()
    {
        var opcoes = Configurar("10.1.2.3");

        opcoes.KnownProxies.ShouldContain(endereco => endereco.ToString() == "10.1.2.3");
        opcoes.ForwardedHeaders.HasFlag(ForwardedHeaders.XForwardedFor).ShouldBeTrue();
    }

    /// <summary>
    /// As redes padrão do ASP.NET (só <c>localhost</c>) saem: em container o proxy nunca é
    /// <c>localhost</c>, e deixá-las ligadas dá a falsa impressão de que algo foi configurado.
    /// </summary>
    [Fact]
    public void Faixa_CIDR_vira_rede_conhecida_e_substitui_as_padrao()
    {
        var opcoes = Configurar("10.0.0.0/8");

        opcoes.KnownIPNetworks.Count.ShouldBe(1);
        opcoes.KnownIPNetworks[0].PrefixLength.ShouldBe(8);
        opcoes.KnownProxies.ShouldBeEmpty();
    }

    /// <summary>Entrada inválida derruba a subida, em vez de virar um proxy silenciosamente ignorado.</summary>
    [Fact]
    public void Entrada_invalida_falha_na_subida()
    {
        Should.Throw<InvalidOperationException>(() => Configurar("nao-e-um-ip"));
    }
}
