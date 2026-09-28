using System.Net;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Hosting;
using Shouldly;

namespace Backend.IntegrationTests.Convites;

/// <summary>
/// As rotas que disputam uma linha só da turma passam por uma fila de concorrência antes do banco.
/// </summary>
/// <remarks>
/// O aceite do link é a rota marcada mais barata de exercitar: um token inexistente responde 404 depois
/// de uma consulta, sem montar turma. Quem ainda não tem turma entra na fila da própria rota.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class FilaPorTurmaTests(ApiFactory fabrica)
{
    private const int Simultaneas = 50;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Com_espera_padrao_a_rajada_entra_na_fila_e_ninguem_toma_429()
    {
        var respostas = await Disparar(fabrica.CreateClient());

        respostas.ShouldAllBe(status => status == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sem_espera_quem_passa_da_vaga_toma_429_em_vez_de_ir_ao_banco()
    {
        await using var apertada = fabrica.WithWebHostBuilder(host =>
            host.UseSetting("RateLimit:FilaPorTurmaSimultaneas", "1").UseSetting("RateLimit:FilaPorTurmaEspera", "0")
        );

        var respostas = await Disparar(apertada.CreateClient());

        respostas.ShouldContain(HttpStatusCode.TooManyRequests);
        respostas.ShouldAllBe(status => status == HttpStatusCode.NotFound || status == HttpStatusCode.TooManyRequests);
    }

    private static async Task<HttpStatusCode[]> Disparar(HttpClient cliente)
    {
        cliente.ComToken((await cliente.RegistrarUsuarioComum(Ct)).AccessToken);

        return await Task.WhenAll(
            Enumerable
                .Range(0, Simultaneas)
                .Select(async _ => (await cliente.PostAsync("/api/v1/convites/token-que-nao-existe/aceitar", null, Ct)).StatusCode)
        );
    }
}
