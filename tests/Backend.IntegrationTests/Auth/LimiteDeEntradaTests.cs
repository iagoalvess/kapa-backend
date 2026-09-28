using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace Backend.IntegrationTests.Auth;

/// <summary>
/// Cadastro e login dividem um balde por IP com rajada, separado do limite estreito das rotas de conta.
/// </summary>
/// <remarks>
/// No <c>TestServer</c> todo cliente sai do mesmo "IP" — é a assembleia que abre o QR do convite no
/// mesmo Wi-Fi. Com a janela fixa de 10 por minuto, a décima primeira pessoa tomava 429 no cadastro.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class LimiteDeEntradaTests(ApiFactory fabrica)
{
    private const int Rajada = 3;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_rajada_cobre_varios_cadastros_do_mesmo_ip_e_depois_acaba()
    {
        await using var apertada = Apertada();
        var cliente = apertada.CreateClient();

        var respostas = new List<HttpStatusCode>();

        for (var i = 0; i <= Rajada; i++)
            respostas.Add(await Registrar(cliente));

        respostas.Take(Rajada).ShouldAllBe(status => status == HttpStatusCode.OK);
        respostas.Last().ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Esgotar_o_balde_da_entrada_nao_trava_o_esqueci_senha()
    {
        await using var apertada = Apertada();
        var cliente = apertada.CreateClient();

        for (var i = 0; i <= Rajada; i++)
            await Registrar(cliente);

        var esqueci = await cliente.PostAsJsonAsync("/api/v1/conta/esqueci-senha", new { email = "alguem@testes.local" }, Ct);

        esqueci.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>Host próprio, com limitador próprio: a cota gasta aqui não vaza para os outros testes.</summary>
    private WebApplicationFactory<Program> Apertada() =>
        fabrica.WithWebHostBuilder(host =>
            host.UseSetting("RateLimit:EntradaRajada", Rajada.ToString(CultureInfo.InvariantCulture)).UseSetting("RateLimit:EntradaPorMinuto", "1")
        );

    private static async Task<HttpStatusCode> Registrar(HttpClient cliente)
    {
        var email = $"usuario-{Guid.CreateVersion7():N}@testes.local";

        var resposta = await cliente.PostAsJsonAsync(
            "/api/v1/auth/registrar",
            await cliente.CorpoDeCadastro("Usuário de Teste", email, "Senha@Teste123", Ct),
            JsonDaApi.Opcoes,
            Ct
        );

        return resposta.StatusCode;
    }
}
