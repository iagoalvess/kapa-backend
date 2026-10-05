using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Assinaturas;
using Backend.Api.DTOs.Convites;
using Backend.Api.DTOs.Formaturas;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Shouldly;

namespace Backend.IntegrationTests.Assinaturas;

/// <summary>
/// O que o plano da turma libera (Sprint 45): a festa fora do gratuito, formando só com plano pago em vigor e a
/// leitura do plano que a tela usa para trancar área.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class PlanoDaTurmaTests(ApiFactory fabrica)
{
    private const string RotaDoPlano = "/api/v1/formaturas/atual/plano";
    private const string Membros = "/api/v1/formaturas/atual/membros";
    private const string Convites = "/api/v1/formaturas/atual/convites";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>As leituras de cada área da festa: a Gestão da turma gratuita bate na política de módulo em todas.</summary>
    public static TheoryData<string> RotasDaFesta =>
        [
            "/api/v1/festa/portaria",
            "/api/v1/festa/painel-de-convites",
            "/api/v1/festa/convites/resumo",
            "/api/v1/festa/convites/meus",
            "/api/v1/loja/compras/resumo",
            "/api/v1/loja/pedidos-de-cancelamento",
        ];

    [Theory]
    [MemberData(nameof(RotasDaFesta))]
    public async Task No_gratuito_a_festa_responde_modulo_nao_incluido(string rota)
    {
        var presidente = await fabrica.NovoMembro(await Gratuita(), PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.GetAsync(rota, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await resposta.Codigo(Ct)).ShouldBe("plano.modulo_nao_incluido");
    }

    [Theory]
    [MemberData(nameof(RotasDaFesta))]
    public async Task No_essencial_a_festa_abre(string rota)
    {
        var presidente = await fabrica.NovoMembro(await Contratada("essencial"), PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.GetAsync(rota, Ct);

        resposta.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
    }

    /// <summary>As mesas são módulo próprio, só do Premium: o Essencial não as tem (decisão de 29/09/2026).</summary>
    [Fact]
    public async Task No_essencial_as_mesas_ficam_de_fora()
    {
        var presidente = await fabrica.NovoMembro(await Contratada("essencial"), PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.GetAsync("/api/v1/festa/mesas", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await resposta.Codigo(Ct)).ShouldBe("plano.modulo_nao_incluido");
    }

    [Fact]
    public async Task O_gratuito_le_o_proprio_plano_sem_a_festa()
    {
        var presidente = await fabrica.NovoMembro(await Gratuita(), PapelNaFormatura.Presidente, Ct);

        var plano = await Ler<PlanoDaTurmaDTO>(await presidente.Cliente.GetAsync(RotaDoPlano, Ct));

        plano.Codigo.ShouldBe(Plano.CodigoGratuito);
        plano.Pago.ShouldBeFalse();
        plano.Modulos.ShouldContain(Modulo.Cobrancas);
        plano.Modulos.ShouldNotContain(Modulo.Festa);
        plano.Modulos.ShouldNotContain(Modulo.Mural);
    }

    /// <summary>Todo membro lê o plano, o formando inclusive: o menu dele também tranca (e esconde) área.</summary>
    [Fact]
    public async Task O_formando_do_essencial_le_o_plano_pago_com_a_festa()
    {
        var formaturaId = await Contratada("essencial");
        await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var plano = await Ler<PlanoDaTurmaDTO>(await formando.Cliente.GetAsync(RotaDoPlano, Ct));

        plano.Codigo.ShouldBe("essencial");
        plano.Pago.ShouldBeTrue();
        plano.Modulos.ShouldContain(Modulo.Festa);
        plano.Modulos.ShouldNotContain(Modulo.Mesas);
        plano.Modulos.ShouldNotContain(Modulo.Mural);
    }

    /// <summary>A troca de papel era a porta lateral do formando no gratuito (F2): agora é a mesma regra do convite.</summary>
    [Fact]
    public async Task No_gratuito_ninguem_vira_formando_pela_troca_de_papel()
    {
        var formaturaId = await Gratuita();
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var comissao = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);

        var formando = await TrocarPapel(presidente, comissao, PapelNaFormatura.Formando);
        var tesoureiro = await TrocarPapel(presidente, comissao, PapelNaFormatura.Tesoureiro);

        formando.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.Codigo(Ct)).ShouldBe("convite.formatura_nao_contratada");
        tesoureiro.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>Pagou um dia e venceu: volta ao gratuito e deixa de convidar e de promover formando (P3).</summary>
    [Fact]
    public async Task Assinatura_vencida_nao_convida_nem_promove_formando()
    {
        var formaturaId = await Contratada("essencial");
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var comissao = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);
        await fabrica.VencerAssinatura(formaturaId, Ct);

        var convite = await presidente.Cliente.PostAsJsonAsync(Convites, new CriarConviteRequestDTO(null, null), Ct);
        var troca = await TrocarPapel(presidente, comissao, PapelNaFormatura.Formando);
        var plano = await Ler<PlanoDaTurmaDTO>(await presidente.Cliente.GetAsync(RotaDoPlano, Ct));

        convite.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await convite.Codigo(Ct)).ShouldBe("convite.formatura_nao_contratada");
        troca.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        plano.Pago.ShouldBeFalse();
        plano.Codigo.ShouldBe(Plano.CodigoGratuito);
    }

    private Task<Guid> Gratuita() => fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct, contratada: false);

    private async Task<Guid> Contratada(string codigo)
    {
        var formaturaId = await Gratuita();
        await fabrica.Contratar(formaturaId, Ct, codigo);

        return formaturaId;
    }

    private static Task<HttpResponseMessage> TrocarPapel(MembroDeTeste quem, MembroDeTeste alvo, string papel) =>
        quem.Cliente.PutAsJsonAsync($"{Membros}/{alvo.UsuarioId}/papel", new AlterarPapelRequestDTO(papel), Json, Ct);

    private static async Task<T> Ler<T>(HttpResponseMessage resposta)
    {
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await resposta.Content.ReadFromJsonAsync<T>(Json, Ct))!;
    }
}
