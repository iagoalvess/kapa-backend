using System.Globalization;
using System.Net;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace Backend.IntegrationTests.Convites;

/// <summary>
/// O limite de <c>/convites/{token}</c> conta por pessoa quando há sessão, e por IP só para o anônimo.
/// </summary>
/// <remarks>
/// No <c>TestServer</c> todo cliente sai do mesmo "IP" — é o Wi-Fi da assembleia em miniatura. Com
/// o limitador antes da autenticação, as duas contas dividiam uma cota só.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class LimiteDeConvitesTests(ApiFactory fabrica)
{
    private const int Rajada = 3;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Com_sessao_cada_conta_tem_a_propria_cota_no_mesmo_ip()
    {
        await using var apertada = Apertada();
        var ana = await ClienteAutenticado(apertada);
        var bruno = await ClienteAutenticado(apertada);

        var daAna = await Consultar(ana, Rajada + 1);
        var doBruno = await Consultar(bruno, 1);

        daAna.Take(Rajada).ShouldAllBe(status => status == HttpStatusCode.NotFound);
        daAna.Last().ShouldBe(HttpStatusCode.TooManyRequests);
        doBruno.ShouldBe([HttpStatusCode.NotFound]);
    }

    [Fact]
    public async Task Sem_sessao_a_cota_e_do_ip_e_a_rajada_acaba()
    {
        await using var apertada = Apertada();

        var respostas = await Consultar(apertada.CreateClient(), Rajada + 1);

        respostas.Take(Rajada).ShouldAllBe(status => status == HttpStatusCode.NotFound);
        respostas.Last().ShouldBe(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// Host próprio, com limitador próprio: a cota gasta aqui não vaza para os outros testes.
    /// </summary>
    /// <remarks>
    /// A reposição é contínua, proporcional ao tempo: com uma ficha por minuto, os poucos segundos
    /// do teste não repõem nenhuma. As duas chaves ficam de fora do <c>ApiFactory</c> — valor dele
    /// e daqui disputariam a mesma chave.
    /// </remarks>
    private WebApplicationFactory<Program> Apertada() =>
        fabrica.WithWebHostBuilder(host =>
            host.UseSetting("RateLimit:ConvitesRajada", Rajada.ToString(CultureInfo.InvariantCulture)).UseSetting("RateLimit:ConvitesPorMinuto", "1")
        );

    private static async Task<HttpClient> ClienteAutenticado(WebApplicationFactory<Program> api)
    {
        var cliente = api.CreateClient();

        return cliente.ComToken((await cliente.RegistrarUsuarioComum(Ct)).AccessToken);
    }

    private static async Task<List<HttpStatusCode>> Consultar(HttpClient cliente, int vezes)
    {
        var respostas = new List<HttpStatusCode>();

        for (var i = 0; i < vezes; i++)
            respostas.Add((await cliente.GetAsync($"/api/v1/convites/inexistente-{i}", Ct)).StatusCode);

        return respostas;
    }
}
