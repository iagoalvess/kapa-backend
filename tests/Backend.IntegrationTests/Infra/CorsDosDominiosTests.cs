using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace Backend.IntegrationTests.Infra;

/// <summary>
/// O CORS dos três domínios (Sprint 33): o app chama tudo com sessão; o site só lê a vitrine.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class CorsDosDominiosTests(ApiFactory fabrica)
{
    private const string App = "https://app.kapa.testes";
    private const string Site = "https://kapa.testes";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/v1/planos")]
    [InlineData("/api/v1/legal/vigentes")]
    [InlineData("/api/v1/privacidade/operadores")]
    public async Task O_site_le_a_vitrine(string caminho)
    {
        var resposta = await Pedir(caminho, Site);

        resposta.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([Site]);
    }

    [Fact]
    public async Task O_site_nao_chama_o_resto_da_api()
    {
        var resposta = await Pedir("/api/v1/auth/refresh", Site, HttpMethod.Options);

        resposta.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Theory]
    [InlineData("/api/v1/planos")]
    [InlineData("/api/v1/auth/refresh")]
    public async Task O_app_chama_a_vitrine_e_o_resto_com_credencial(string caminho)
    {
        var resposta = await Pedir(caminho, App, HttpMethod.Options);

        resposta.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([App]);
        resposta.Headers.GetValues("Access-Control-Allow-Credentials").ShouldBe(["true"]);
    }

    [Fact]
    public async Task Outra_origem_nao_le_nem_a_vitrine()
    {
        var resposta = await Pedir("/api/v1/planos", "https://golpe.testes");

        resposta.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    /// <summary>Um pedido de origem cruzada; <c>OPTIONS</c> é a consulta prévia de um <c>POST</c>.</summary>
    private async Task<HttpResponseMessage> Pedir(string caminho, string origem, HttpMethod? metodo = null)
    {
        await using var api = fabrica.WithWebHostBuilder(host => host.UseSetting("Cors:Origens:0", App).UseSetting("Cors:OrigensDoSite:0", Site));
        var cliente = api.CreateClient();

        using var pedido = new HttpRequestMessage(metodo ?? HttpMethod.Get, caminho);
        pedido.Headers.Add("Origin", origem);
        if (pedido.Method == HttpMethod.Options)
            pedido.Headers.Add("Access-Control-Request-Method", caminho.EndsWith("refresh", StringComparison.Ordinal) ? "POST" : "GET");

        return await cliente.SendAsync(pedido, Ct);
    }
}
