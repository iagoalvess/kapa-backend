using System.Net;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Shouldly;

namespace Backend.IntegrationTests.Desempenho;

/// <summary>
/// Guarda de N+1: a rota quente não pode disparar uma consulta por item.
/// </summary>
/// <remarks>
/// O teto é medido, não chutado: é o número que a rota faz hoje mais a folga de uma consulta. Regressão
/// de N+1 passa a aparecer no CI, e não na primeira turma. Se uma rota legitimamente crescer, o teto
/// sobe <b>com o número anotado</b> — o teste não se remove.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class GuardaDeConsultasTests(ApiFactory fabrica)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Painel_publico_respeita_o_teto_de_consultas()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        fabrica.Contador.Zerar();
        var resposta = await presidente.Cliente.GetAsync("/api/v1/dashboard/publico", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Linha de base medida em 03/10/2026: 5 comandos. O teto é o atual mais a folga de um;
        // ao subir, sobe com o número anotado — o teste não se remove.
        fabrica.Contador.Comandos.ShouldBeGreaterThan(0);
        fabrica.Contador.Comandos.ShouldBeLessThanOrEqualTo(6);
    }
}
