using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Api.DTOs.Adesoes;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Convites;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using static Backend.IntegrationTests.Infra.AdesaoDeTeste;

namespace Backend.IntegrationTests.Cobrancas;

/// <summary>
/// Desconto, isenção e "apenas gestão" (Sprint 54) contra a API e o Postgres: a condição concedida pela tesouraria
/// vira preço líquido na prévia, no aceite e nas parcelas; a isenção contrata sem dívida; o aditivo não cobra de volta.
/// </summary>
/// <remarks>Catálogo: Festa 15 (R$ 3.000), Festa 20 (R$ 3.600) e Fotos (R$ 600), em 10×.</remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed partial class CondicaoFinanceiraEndpointsTests(ApiFactory fabrica)
{
    private const string Condicoes = "/api/v1/cobrancas/condicoes";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ItemDeCobrancaRequestDTO Festa(string nome, long valor, int convites) =>
        new(TipoDeCobranca.Festa, nome, valor, 10, 10, DataUtils.Hoje().AddMonths(1), Grupo: "Festa", ConvitesDaFesta: convites);

    private async Task<(TurmaDeTeste Turma, Guid Festa15, Guid Festa20, Guid Fotos)> Catalogo()
    {
        var (turma, pacotes) = await fabrica.TurmaComCatalogo(
            Festa("Festa 15", 300_000, 15),
            Festa("Festa 20", 360_000, 20),
            new ItemDeCobrancaRequestDTO(TipoDeCobranca.FotoEAlbum, "Fotos", 60_000, 10, 10, DataUtils.Hoje().AddMonths(1))
        );

        return (turma, pacotes[0], pacotes[1], pacotes[2]);
    }

    private static CondicaoFinanceiraRequestDTO Isencao(Guid pacote) => new([new BeneficioDaCondicaoDTO(pacote, BeneficioDaCondicao.Inteiro, null)]);

    private static async Task<T> Ler<T>(HttpResponseMessage resposta)
    {
        resposta.IsSuccessStatusCode.ShouldBeTrue(await resposta.Content.ReadAsStringAsync(Ct));

        return (await resposta.Content.ReadFromJsonAsync<T>(Json, Ct))!;
    }

    /// <summary>
    /// Cenário 3: a comissão isenta da festa escolhe, aceita e contrata sem parcela; subir de faixa cobra só a diferença
    /// de catálogo que a isenção "até a Festa 15" não cobre.
    /// </summary>
    [Fact]
    public async Task Comissao_isenta_adere_sem_parcela_e_o_aditivo_cobra_so_o_que_passa_da_faixa()
    {
        var (turma, festa15, festa20, fotos) = await Catalogo();
        var comissao = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Comissao, Ct);

        (await comissao.Cliente.PutAsJsonAsync($"{Condicoes}/membros/{comissao.UsuarioId}", Isencao(festa15), Json, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.Forbidden
        );
        var concedida = await Ler<CondicaoDaPessoaDTO>(
            await turma.Presidente.Cliente.PutAsJsonAsync($"{Condicoes}/membros/{comissao.UsuarioId}", Isencao(festa15), Json, Ct)
        );
        concedida.Condicao!.Beneficios.ShouldHaveSingleItem().DescontoEmCentavos.ShouldBe(300_000);
        (await Ler<CondicaoDaPessoaDTO>(await comissao.Cliente.GetAsync($"{Condicoes}/membros/{comissao.UsuarioId}", Ct))).Condicao.ShouldNotBeNull();

        var conteudo = (await comissao.Cliente.GetFromJsonAsync<ConteudoParaAdesaoDTO>(Termo([festa15]), Json, Ct))!;
        conteudo.TemCondicao.ShouldBeTrue();
        conteudo.Catalogo.Single(p => p.Id == festa15).DescontoEmCentavos.ShouldBe(300_000);
        conteudo.Catalogo.Single(p => p.Id == festa20).DescontoEmCentavos.ShouldBe(300_000);
        conteudo.Catalogo.Single(p => p.Id == fotos).DescontoEmCentavos.ShouldBe(0);
        conteudo.Plano!.TotalEmCentavos.ShouldBe(0);
        conteudo.Plano.Parcelas.ShouldBeEmpty();
        conteudo.Plano.Descontos.ShouldHaveSingleItem().DescontoEmCentavos.ShouldBe(300_000);

        await PreencherCadastro(comissao.Cliente, NovoCpf());
        (await Aderir(fabrica, comissao.Cliente, pacotes: [festa15])).Resposta.StatusCode.ShouldBe(HttpStatusCode.Created);

        await using (var contexto = fabrica.ContextoDe(turma.FormaturaId))
        {
            (await contexto.Parcelas.CountAsync(Ct)).ShouldBe(0);
            (await contexto.EscolhasDaCesta.SingleAsync(Ct)).DescontoEmCentavos.ShouldBe(300_000);
        }

        var cesta = await Ler<MinhaCestaDTO>(await comissao.Cliente.GetAsync("/api/v1/adesoes/minha-cesta", Ct));
        cesta.Pacotes.ShouldHaveSingleItem().DescontoEmCentavos.ShouldBe(300_000);
        cesta.Disponiveis.Single(p => p.ItemDeCobrancaId == festa20).DiferencaEmCentavos.ShouldBe(60_000);
        cesta.Disponiveis.Single(p => p.ItemDeCobrancaId == fotos).DiferencaEmCentavos.ShouldBe(60_000);

        var previa = await Ler<PreviaDoAditivoDTO>(
            await comissao.Cliente.PostAsJsonAsync("/api/v1/adesoes/aditivo/previa", new SimularAditivoRequestDTO([festa20]), Json, Ct)
        );
        (await comissao.Cliente.PostAsync("/api/v1/adesoes/aditivo/codigo", null, Ct)).EnsureSuccessStatusCode();
        var codigo = SeisDigitos().Match((await fabrica.UltimoEmailDaFila(Ct))!).Value;
        (
            await comissao.Cliente.PostAsJsonAsync(
                "/api/v1/adesoes/aditivo",
                new AceitarAditivoRequestDTO([festa20], previa.HashDoConteudo, codigo),
                Json,
                Ct
            )
        ).EnsureSuccessStatusCode();

        previa.TotalEmCentavos.ShouldBe(60_000);
        await using var depois = fabrica.ContextoDe(turma.FormaturaId);
        (await depois.Parcelas.SumAsync(p => p.ValorOriginalEmCentavos, Ct)).ShouldBe(60_000);
        (await depois.EscolhasDaCesta.SingleAsync(Ct)).DescontoEmCentavos.ShouldBe(300_000);
    }

    /// <summary>Cenário 2: desconto parcial vira preço líquido; trocar a condição com a prévia aberta exige ler de novo.</summary>
    [Fact]
    public async Task Desconto_parcial_vira_parcela_liquida_e_mudar_a_condicao_desatualiza_a_previa()
    {
        var (turma, festa15, _, fotos) = await Catalogo();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf());
        var vinteporcento = new CondicaoFinanceiraRequestDTO([new BeneficioDaCondicaoDTO(festa15, 2_000, null)]);
        await Ler<CondicaoDaPessoaDTO>(
            await turma.Presidente.Cliente.PutAsJsonAsync($"{Condicoes}/membros/{formando.UsuarioId}", vinteporcento, Json, Ct)
        );
        var antiga = (await formando.Cliente.GetFromJsonAsync<ConteudoParaAdesaoDTO>(Termo([festa15, fotos]), Json, Ct))!;

        var fixo = vinteporcento with { Beneficios = [new BeneficioDaCondicaoDTO(festa15, null, 50_000)] };
        await Ler<CondicaoDaPessoaDTO>(await turma.Presidente.Cliente.PutAsJsonAsync($"{Condicoes}/membros/{formando.UsuarioId}", fixo, Json, Ct));
        var desatualizada = await formando.Cliente.PostAsJsonAsync(
            "/api/v1/adesoes",
            new AderirRequestDTO(antiga.HashDoConteudo, await PedirCodigo(fabrica, formando.Cliente), [festa15, fotos]),
            Json,
            Ct
        );
        var (aceite, _) = await Aderir(fabrica, formando.Cliente, pacotes: [festa15, fotos]);

        antiga.Plano!.TotalEmCentavos.ShouldBe(300_000);
        (await desatualizada.Codigo(Ct)).ShouldBe("adesao.termo_desatualizado");
        aceite.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        (await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == festa15).SumAsync(p => p.ValorOriginalEmCentavos, Ct)).ShouldBe(250_000);
        (await contexto.Parcelas.Where(p => p.ItemDeCobrancaId == fotos).SumAsync(p => p.ValorOriginalEmCentavos, Ct)).ShouldBe(60_000);
    }

    /// <summary>A condição preparada no convite pessoal passa ao vínculo de quem aceita com aquele e-mail.</summary>
    [Fact]
    public async Task Condicao_preparada_no_convite_passa_para_quem_aceita()
    {
        var (turma, festa15, _, _) = await Catalogo();
        var email = $"convidado.{Guid.NewGuid():N}@kapa.dev";
        var criado = await Ler<ConviteCriadoDTO>(
            await turma.Presidente.Cliente.PostAsJsonAsync(
                "/api/v1/formaturas/atual/convites",
                new CriarConviteRequestDTO(email, PapelNaFormatura.Comissao),
                Json,
                Ct
            )
        );
        await Ler<CondicaoDaPessoaDTO>(
            await turma.Presidente.Cliente.PutAsJsonAsync(
                $"{Condicoes}/convidados?email={Uri.EscapeDataString(email.ToUpperInvariant())}",
                Isencao(festa15),
                Json,
                Ct
            )
        );

        var cliente = fabrica.CreateClient();
        cliente.ComToken((await cliente.RegistrarComEmail(email, Ct)).AccessToken);
        await fabrica.ConfirmarEmail(email, Ct);
        var aceite = await cliente.PostAsync(
            $"/api/v1/convites/{criado.Link.Split('/').Last()}/aceitar",
            new StringContent("{}", Encoding.UTF8, "application/json"),
            Ct
        );
        aceite.StatusCode.ShouldBe(HttpStatusCode.OK, await aceite.Content.ReadAsStringAsync(Ct));

        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var condicao = await contexto.CondicoesFinanceiras.SingleAsync(Ct);
        condicao.Email.ShouldBeNull();
        condicao.VinculoId.ShouldBe(
            (await contexto.Vinculos.SingleAsync(v => v.FormaturaId == turma.FormaturaId && v.Papel == PapelNaFormatura.Comissao, Ct)).Id
        );
    }

    /// <summary>Cenário 4: a comissão marca "apenas gestão" e sai da conta de quem falta aderir; o formando não pode.</summary>
    [Fact]
    public async Task Apenas_gestao_sai_da_conta_de_quem_falta_aderir_e_formando_nao_marca()
    {
        var (turma, _, _, _) = await Catalogo();
        var comissao = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Comissao, Ct);
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        var antes = await Ler<ResumoDeAdesoesDTO>(await turma.Presidente.Cliente.GetAsync("/api/v1/adesoes/resumo", Ct));

        var marcar = await comissao.Cliente.PutAsJsonAsync("/api/v1/adesoes/apenas-gestao", new ApenasGestaoRequestDTO(true), Json, Ct);
        var doFormando = await formando.Cliente.PutAsJsonAsync("/api/v1/adesoes/apenas-gestao", new ApenasGestaoRequestDTO(true), Json, Ct);
        var depois = await Ler<ResumoDeAdesoesDTO>(await turma.Presidente.Cliente.GetAsync("/api/v1/adesoes/resumo", Ct));
        var conteudo = (await comissao.Cliente.GetFromJsonAsync<ConteudoParaAdesaoDTO>(Termo([]), Json, Ct))!;

        marcar.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        doFormando.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        depois.Membros.ShouldBe(antes.Membros - 1);
        conteudo.ApenasGestao.ShouldBeTrue();
    }

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex SeisDigitos();
}
