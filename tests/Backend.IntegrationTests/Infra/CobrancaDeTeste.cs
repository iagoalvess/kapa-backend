using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Adesoes;
using Backend.Api.DTOs.Cobrancas;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Formaturas.Models;
using Shouldly;
using static Backend.IntegrationTests.Infra.AdesaoDeTeste;

namespace Backend.IntegrationTests.Infra;

/// <summary>Uma turma com plano vigente e termo publicado, e quem a preside.</summary>
/// <param name="FormaturaId">Turma.</param>
/// <param name="Presidente">Presidente — é da Tesouraria e da Gestão.</param>
public sealed record TurmaDeTeste(Guid FormaturaId, MembroDeTeste Presidente);

/// <summary>
/// Monta o mínimo para haver adesão e pedido: plano vigente, termo publicado e formando que aderiu.
/// </summary>
/// <remarks>Dos testes de pedido (Sprint 20) e do convite da festa (Sprint 21), que partem do mesmo lugar.</remarks>
public static class CobrancaDeTeste
{
    private const string Termo = "# Termo da turma\n\nO formando concorda com o plano de cobrança.";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Uma turma ativa com uma mensalidade no catálogo vigente e o termo publicado.</summary>
    /// <remarks>A mensalidade é um pacote como outro qualquer (Sprint 47): quem adere pelo helper a põe na cesta.</remarks>
    /// <param name="fabrica">API de teste.</param>
    public static async Task<TurmaDeTeste> TurmaComPlano(this ApiFactory fabrica) =>
        (
            await fabrica.TurmaComCatalogo(
                new ItemDeCobrancaRequestDTO(TipoDeCobranca.Mensalidade, null, 240_000, 12, 10, DataUtils.Hoje().AddMonths(1))
            )
        ).Turma;

    /// <summary>Uma turma ativa com os pacotes informados no catálogo vigente e o termo publicado.</summary>
    /// <param name="fabrica">API de teste.</param>
    /// <param name="pacotes">Os pacotes, na ordem do catálogo.</param>
    /// <returns>A turma e o id de cada pacote, na mesma ordem.</returns>
    public static async Task<(TurmaDeTeste Turma, IReadOnlyList<Guid> Pacotes)> TurmaComCatalogo(
        this ApiFactory fabrica,
        params ItemDeCobrancaRequestDTO[] pacotes
    )
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var criacao = await presidente.Cliente.PostAsJsonAsync(
            "/api/v1/cobrancas/planos",
            new PlanoDeCobrancaRequestDTO("Plano 2027", 0, 0, 0, 0, 0),
            Json,
            Ct
        );
        var plano = (await criacao.Content.ReadFromJsonAsync<PlanoDeCobrancaDTO>(Json, Ct))!;
        var ids = new List<Guid>();

        foreach (var pacote in pacotes)
        {
            var incluido = await presidente.Cliente.PostAsJsonAsync($"/api/v1/cobrancas/planos/{plano.Id}/itens", pacote, Json, Ct);
            incluido.EnsureSuccessStatusCode();
            ids.Add((await incluido.Content.ReadFromJsonAsync<PlanoDeCobrancaDTO>(Json, Ct))!.Itens[^1].Id);
        }

        (await presidente.Cliente.PostAsync($"/api/v1/cobrancas/planos/{plano.Id}/vigorar", null, Ct)).EnsureSuccessStatusCode();
        (await presidente.Cliente.PostAsJsonAsync("/api/v1/adesoes/termos", new PublicarTermoRequestDTO(Termo), Json, Ct)).EnsureSuccessStatusCode();

        return (new TurmaDeTeste(formaturaId, presidente), ids);
    }

    /// <summary>Um formando com adesão assinada — o único que pode pedir.</summary>
    /// <param name="fabrica">API de teste.</param>
    /// <param name="formaturaId">Turma.</param>
    /// <param name="pacotes">A cesta; vazia, o primeiro pacote do catálogo.</param>
    public static async Task<MembroDeTeste> FormandoComAdesao(this ApiFactory fabrica, Guid formaturaId, params Guid[] pacotes)
    {
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf());
        (await Aderir(fabrica, formando.Cliente, pacotes: pacotes.Length > 0 ? pacotes : null)).Resposta.StatusCode.ShouldBe(HttpStatusCode.Created);

        return formando;
    }
}
