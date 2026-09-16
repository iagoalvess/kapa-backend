using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Api.DTOs.Adesoes;
using Backend.Api.DTOs.Cobrancas;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Formandos;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using static Backend.IntegrationTests.Infra.AdesaoDeTeste;

namespace Backend.IntegrationTests.Adesoes;

/// <summary>
/// A adesão contra a API e o Postgres de verdade: quem pode chamar cada endpoint e os critérios de
/// aceite da Sprint 7 — prova gravada, transação única, snapshot imutável, PDF estável.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class AdesaoEndpointsTests(ApiFactory fabrica)
{
    private const string Rota = "/api/v1/adesoes";

    private const string TermoV1 = "# Termo de adesão\n\nA turma contrata a formatura e divide o custo entre os formandos.";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Uma turma ativa com plano em vigor (R$ 2.400,00 em 12×) e termo publicado.</summary>
    private sealed record Turma(Guid FormaturaId, MembroDeTeste Presidente, Guid PlanoId);

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.Forbidden, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    public async Task Termos_sao_do_presidente_e_o_painel_e_da_gestao(string papel, HttpStatusCode termos, HttpStatusCode painel)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        (await membro.Cliente.GetAsync($"{Rota}/termos", Ct)).StatusCode.ShouldBe(termos);
        (await membro.Cliente.GetAsync(Rota, Ct)).StatusCode.ShouldBe(painel);
        (await membro.Cliente.GetAsync($"{Rota}/resumo", Ct)).StatusCode.ShouldBe(painel);
        (await membro.Cliente.GetAsync($"{Rota}/termos/vigente", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await membro.Cliente.GetAsync($"{Rota}/eu", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Sem termo e sem plano a tela explica o que falta: as duas partes vêm ausentes, e o aceite é recusado.</summary>
    [Fact]
    public async Task Sem_termo_nem_plano_o_conteudo_vem_vazio_e_o_aceite_e_recusado()
    {
        var formando = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Formando, Ct);

        var conteudo = await formando.Cliente.GetFromJsonAsync<ConteudoParaAdesaoDTO>($"{Rota}/termos/vigente", Json, Ct);
        var aceite = await formando.Cliente.PostAsJsonAsync(Rota, new AderirRequestDTO(new string('a', 64), "123456"), Json, Ct);

        conteudo!.Termo.ShouldBeNull();
        conteudo.Plano.ShouldBeNull();
        conteudo.HashDoConteudo.ShouldBeNull();
        aceite.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await aceite.Codigo(Ct)).ShouldBe("adesao.sem_termo_publicado");
    }

    [Fact]
    public async Task Aceite_grava_versao_hash_data_ip_e_user_agent_e_gera_as_parcelas()
    {
        var turma = await TurmaPronta();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf());
        await using var comIp = fabrica.WithWebHostBuilder(host =>
            host.ConfigureTestServices(servicos => servicos.AddSingleton<IStartupFilter>(new IpFixo("203.0.113.7")))
        );
        var cliente = comIp.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        cliente.DefaultRequestHeaders.Authorization = formando.Cliente.DefaultRequestHeaders.Authorization;
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("NavegadorDeTeste/1.0");
        var antes = DateTime.UtcNow;

        var (resposta, hash) = await Aderir(fabrica, cliente);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        var adesao = (await resposta.Content.ReadFromJsonAsync<AdesaoDTO>(Json, Ct))!;
        adesao.Versao.ShouldBe(1);
        adesao.Plano.TotalEmCentavos.ShouldBe(240_000);
        await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
        var gravada = await contexto.Adesoes.SingleAsync(a => a.Id == adesao.Id, Ct);
        gravada.HashDoConteudo.ShouldBe(hash);
        gravada.EnderecoIp.ShouldBe("203.0.113.7");
        gravada.UserAgent.ShouldBe("NavegadorDeTeste/1.0");
        gravada.AceitoEm.ShouldBeInRange(antes.AddSeconds(-1), DateTime.UtcNow);
        (await Parcelas(turma.FormaturaId, formando.UsuarioId)).ShouldBe(12);
    }

    [Fact]
    public async Task Sem_nome_e_cpf_no_cadastro_devolve_409_e_nao_gera_parcela()
    {
        var turma = await TurmaPronta();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);

        var minha = await formando.Cliente.GetFromJsonAsync<MinhaAdesaoDTO>($"{Rota}/eu", Json, Ct);
        var (resposta, _) = await Aderir(fabrica, formando.Cliente);

        minha!.Pendencias.ShouldBe(["nomeCompleto", "cpf", "dataDeNascimento"]);
        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("adesao.cadastro_incompleto");
        (await Parcelas(turma.FormaturaId, formando.UsuarioId)).ShouldBe(0);
    }

    [Fact]
    public async Task Hash_divergente_devolve_409_e_nao_gera_parcela()
    {
        var turma = await TurmaPronta();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf());

        var resposta = await formando.Cliente.PostAsJsonAsync(
            Rota,
            new AderirRequestDTO(new string('0', 64), await PedirCodigo(fabrica, formando.Cliente)),
            Json,
            Ct
        );

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("adesao.termo_desatualizado");
        (await Parcelas(turma.FormaturaId, formando.UsuarioId)).ShouldBe(0);
    }

    /// <summary>A comissão publica a v2 enquanto o formando lê a v1: o aceite da v1 é recusado.</summary>
    [Fact]
    public async Task Versao_nova_publicada_durante_a_leitura_invalida_o_hash_lido()
    {
        var turma = await TurmaPronta();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf());
        var lido = await formando.Cliente.GetFromJsonAsync<ConteudoParaAdesaoDTO>($"{Rota}/termos/vigente", Json, Ct);
        await Publicar(turma.Presidente.Cliente, $"{TermoV1}\n\nCláusula nova.");

        var resposta = await formando.Cliente.PostAsJsonAsync(
            Rota,
            new AderirRequestDTO(lido!.HashDoConteudo, await PedirCodigo(fabrica, formando.Cliente)),
            Json,
            Ct
        );

        (await resposta.Codigo(Ct)).ShouldBe("adesao.termo_desatualizado");
    }

    /// <summary>O clique duplo no "Aceito": o mesmo código, duas vezes — a segunda é recusada.</summary>
    [Fact]
    public async Task Aderir_duas_vezes_devolve_409_sem_duplicar_parcelas()
    {
        var turma = await TurmaPronta();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf());
        var codigo = await PedirCodigo(fabrica, formando.Cliente);

        (await Aderir(fabrica, formando.Cliente, codigo)).Resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        var (segunda, _) = await Aderir(fabrica, formando.Cliente, codigo);

        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await segunda.Codigo(Ct)).ShouldBe("adesao.ja_aderiu");
        (await Parcelas(turma.FormaturaId, formando.UsuarioId)).ShouldBe(12);
    }

    /// <summary>
    /// Derruba a geração das parcelas no banco — um gatilho recusa o <c>INSERT</c> delas para este
    /// vínculo — e confere que a adesão também não ficou: é uma transação só.
    /// </summary>
    [Fact]
    public async Task Geracao_derrubada_leva_a_adesao_junto()
    {
        var turma = await TurmaPronta();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf());
        var vinculoId = await VinculoDe(turma.FormaturaId, formando.UsuarioId);
        var gatilho = $"derruba_parcelas_{vinculoId:N}";
        var criar =
            $"CREATE TRIGGER {gatilho} BEFORE INSERT ON parcelas FOR EACH ROW WHEN (NEW.vinculo_id = '{vinculoId}') EXECUTE FUNCTION recusar_alteracao()";
        var remover = $"DROP TRIGGER {gatilho} ON parcelas";

        await using (var contexto = fabrica.ContextoDe(null))
            await contexto.Database.ExecuteSqlRawAsync(criar, Ct);

        try
        {
            var (resposta, _) = await Aderir(fabrica, formando.Cliente);

            resposta.IsSuccessStatusCode.ShouldBeFalse();
            await using var contexto = fabrica.ContextoDe(turma.FormaturaId);
            (await contexto.Adesoes.CountAsync(a => a.VinculoId == vinculoId, Ct)).ShouldBe(0);
            (await Parcelas(turma.FormaturaId, formando.UsuarioId)).ShouldBe(0);
        }
        finally
        {
            await using var contexto = fabrica.ContextoDe(null);
            await contexto.Database.ExecuteSqlRawAsync(remover, Ct);
        }
    }

    /// <summary>
    /// O aceite congela tudo: publicar a v2, mudar o valor do plano e corrigir nome e CPF no cadastro
    /// não mudam a adesão nem o PDF dela.
    /// </summary>
    [Fact]
    public async Task Termo_novo_plano_novo_e_cadastro_corrigido_nao_mudam_adesao_nem_pdf()
    {
        var turma = await TurmaPronta();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        var cpf = NovoCpf();
        await PreencherCadastro(formando.Cliente, cpf);
        var adesao = (await (await Aderir(fabrica, formando.Cliente)).Resposta.Content.ReadFromJsonAsync<AdesaoDTO>(Json, Ct))!;
        var pdfAntes = await formando.Cliente.GetByteArrayAsync($"{Rota}/{adesao.Id}/pdf", Ct);

        await Publicar(turma.Presidente.Cliente, $"{TermoV1}\n\nVersão 2.");
        var plano = await turma.Presidente.Cliente.GetFromJsonAsync<PlanoDeCobrancaDTO>($"/api/v1/cobrancas/planos/{turma.PlanoId}", Json, Ct);
        (
            await turma.Presidente.Cliente.PutAsJsonAsync(
                $"/api/v1/cobrancas/planos/{turma.PlanoId}/itens/{plano!.Itens[0].Id}",
                Mensalidade(valor: 480_000),
                Json,
                Ct
            )
        ).EnsureSuccessStatusCode();
        await PreencherCadastro(formando.Cliente, NovoCpf(), nome: "Ana Souza Lima");

        var depois = await formando.Cliente.GetFromJsonAsync<MinhaAdesaoDTO>($"{Rota}/eu", Json, Ct);
        var pdfDepois = await formando.Cliente.GetByteArrayAsync($"{Rota}/{adesao.Id}/pdf", Ct);

        depois!.Adesao!.Versao.ShouldBe(1);
        depois.Adesao.NomeCompleto.ShouldBe("Ana Souza");
        depois.Adesao.Cpf.ShouldBe(cpf);
        depois.Adesao.Plano.TotalEmCentavos.ShouldBe(240_000);
        depois.Adesao.ConteudoDoTermo.ShouldBe(TermoV1);
        pdfDepois.ShouldBe(pdfAntes);
    }

    /// <summary>
    /// O código de confirmação, do pedido ao registro: sem ele não há aceite, e o aceite grava o
    /// e-mail que o recebeu — é o que o PDF mostra como prova de que a caixa de entrada confirmou.
    /// </summary>
    [Fact]
    public async Task Aceite_exige_o_codigo_do_email_e_grava_o_endereco_que_o_recebeu()
    {
        var turma = await TurmaPronta();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf());

        var (semCodigo, _) = await Aderir(fabrica, formando.Cliente, codigo: "000000");

        semCodigo.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await semCodigo.Codigo(Ct)).ShouldBe("adesao.codigo_invalido");
        (await Parcelas(turma.FormaturaId, formando.UsuarioId)).ShouldBe(0);

        var (comCodigo, _) = await Aderir(fabrica, formando.Cliente);

        comCodigo.StatusCode.ShouldBe(HttpStatusCode.Created);
        var adesao = (await comCodigo.Content.ReadFromJsonAsync<AdesaoDTO>(Json, Ct))!;
        adesao.EmailDoAceite.ShouldNotBeNullOrEmpty();

        var envio = await formando.Cliente.PostAsync($"{Rota}/codigo", null, Ct);
        (await envio.Codigo(Ct)).ShouldBe("adesao.ja_aderiu");
    }

    /// <summary>Quem aceitou a v1 pode aceitar a v2 quando a comissão pedir; as parcelas não duplicam.</summary>
    [Fact]
    public async Task Readesao_a_versao_nova_nao_duplica_parcelas()
    {
        var turma = await TurmaPronta();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf());
        await Aderir(fabrica, formando.Cliente);
        await Publicar(turma.Presidente.Cliente, $"{TermoV1}\n\nVersão 2.");

        var (resposta, _) = await Aderir(fabrica, formando.Cliente);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await resposta.Content.ReadFromJsonAsync<AdesaoDTO>(Json, Ct))!.Versao.ShouldBe(2);
        (await Parcelas(turma.FormaturaId, formando.UsuarioId)).ShouldBe(12);
        var termos = await turma.Presidente.Cliente.GetFromJsonAsync<List<TermoPublicadoDTO>>($"{Rota}/termos", Json, Ct);
        termos!.Select(t => (t.Versao, t.Adesoes)).ShouldBe([(2, 1), (1, 1)]);
    }

    [Fact]
    public async Task Pdf_e_do_proprio_e_da_gestao_e_formando_nao_ve_o_de_outro()
    {
        var turma = await TurmaPronta();
        var ana = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        var bruno = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        var comissao = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Comissao, Ct);
        await PreencherCadastro(ana.Cliente, NovoCpf());
        var adesao = (await (await Aderir(fabrica, ana.Cliente)).Resposta.Content.ReadFromJsonAsync<AdesaoDTO>(Json, Ct))!;

        var daPropria = await ana.Cliente.GetAsync($"{Rota}/{adesao.Id}/pdf", Ct);
        var deOutro = await bruno.Cliente.GetAsync($"{Rota}/{adesao.Id}/pdf", Ct);
        var daComissao = await comissao.Cliente.GetAsync($"{Rota}/{adesao.Id}/pdf", Ct);
        var minhaDeBruno = await bruno.Cliente.GetFromJsonAsync<MinhaAdesaoDTO>($"{Rota}/eu", Json, Ct);

        daPropria.StatusCode.ShouldBe(HttpStatusCode.OK);
        daPropria.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        deOutro.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        daComissao.StatusCode.ShouldBe(HttpStatusCode.OK);
        minhaDeBruno!.Adesao.ShouldBeNull();
    }

    /// <summary>Decisão de 14/09/2026: o mesmo CPF não adere duas vezes na turma — em outra turma, pode.</summary>
    [Fact]
    public async Task Cpf_ja_usado_na_turma_devolve_409()
    {
        var turma = await TurmaPronta();
        var ana = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        var outraConta = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        var outraTurma = await TurmaPronta();
        var naOutraTurma = await fabrica.NovoMembro(outraTurma.FormaturaId, PapelNaFormatura.Formando, Ct);
        var cpf = NovoCpf();
        await PreencherCadastro(ana.Cliente, cpf);
        await PreencherCadastro(outraConta.Cliente, cpf);
        await PreencherCadastro(naOutraTurma.Cliente, cpf);
        await Aderir(fabrica, ana.Cliente);

        var (repetido, _) = await Aderir(fabrica, outraConta.Cliente);
        var (emOutraTurma, _) = await Aderir(fabrica, naOutraTurma.Cliente);

        (await repetido.Codigo(Ct)).ShouldBe("adesao.cpf_em_uso");
        emOutraTurma.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary>Decisão de 14/09/2026: menor de 18 não adere pela plataforma.</summary>
    [Fact]
    public async Task Menor_de_18_devolve_409()
    {
        var turma = await TurmaPronta();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf(), nascimento: DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-17));

        var (resposta, _) = await Aderir(fabrica, formando.Cliente);

        (await resposta.Codigo(Ct)).ShouldBe("adesao.menor_de_idade");
    }

    [Fact]
    public async Task Painel_mostra_quem_aderiu_e_quem_falta_e_lembra_quem_falta()
    {
        var turma = await TurmaPronta();
        var ana = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        var bruno = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(ana.Cliente, NovoCpf(), nome: "Ana Sônia Souza");
        await Aderir(fabrica, ana.Cliente);

        var resumo = await turma.Presidente.Cliente.GetFromJsonAsync<ResumoDeAdesoesDTO>($"{Rota}/resumo", Json, Ct);
        var faltam = await turma.Presidente.Cliente.GetFromJsonAsync<PaginaDTO<SituacaoDeAdesaoDTO>>($"{Rota}?aderiu=false", Json, Ct);
        var aderiram = await turma.Presidente.Cliente.GetFromJsonAsync<PaginaDTO<SituacaoDeAdesaoDTO>>($"{Rota}?aderiu=true", Json, Ct);
        var lembrete = await turma.Presidente.Cliente.PostAsync($"{Rota}/{bruno.UsuarioId}/lembrete", null, Ct);
        var lembreteDeQuemAderiu = await turma.Presidente.Cliente.PostAsync($"{Rota}/{ana.UsuarioId}/lembrete", null, Ct);
        var semAcento = await turma.Presidente.Cliente.GetFromJsonAsync<PaginaDTO<SituacaoDeAdesaoDTO>>($"{Rota}?busca=sonia", Json, Ct);

        resumo.ShouldBe(new ResumoDeAdesoesDTO(3, 1, 1));
        faltam!.Itens.Select(s => s.UsuarioId).ShouldBe([turma.Presidente.UsuarioId, bruno.UsuarioId], ignoreOrder: true);
        aderiram!.Itens.ShouldHaveSingleItem().Nome.ShouldBe("Ana Sônia Souza");
        semAcento!.Itens.ShouldHaveSingleItem().UsuarioId.ShouldBe(ana.UsuarioId);
        lembrete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        lembreteDeQuemAderiu.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>Adesão e termo são prova: o banco recusa alterar ou apagar, mesmo por SQL escrito à mão.</summary>
    [Fact]
    public async Task Banco_recusa_alterar_adesao_e_termo()
    {
        var turma = await TurmaPronta();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        await PreencherCadastro(formando.Cliente, NovoCpf());
        var adesao = (await (await Aderir(fabrica, formando.Cliente)).Resposta.Content.ReadFromJsonAsync<AdesaoDTO>(Json, Ct))!;

        await using var contexto = fabrica.ContextoDe(null);
        var naAdesao = await Should.ThrowAsync<PostgresException>(() =>
            contexto.Database.ExecuteSqlAsync($"UPDATE adesoes SET nome_completo = 'Outro' WHERE id = {adesao.Id}", Ct)
        );
        var noTermo = await Should.ThrowAsync<PostgresException>(() =>
            contexto.Database.ExecuteSqlAsync($"DELETE FROM termos_de_adesao WHERE formatura_id = {turma.FormaturaId}", Ct)
        );

        naAdesao.MessageText.ShouldContain("append-only");
        noTermo.MessageText.ShouldContain("append-only");
    }

    [Fact]
    public async Task Cpf_da_adesao_sai_cifrado_na_coluna()
    {
        var turma = await TurmaPronta();
        var formando = await fabrica.NovoMembro(turma.FormaturaId, PapelNaFormatura.Formando, Ct);
        var cpf = NovoCpf();
        await PreencherCadastro(formando.Cliente, cpf);
        var adesao = (await (await Aderir(fabrica, formando.Cliente)).Resposta.Content.ReadFromJsonAsync<AdesaoDTO>(Json, Ct))!;

        await using var contexto = fabrica.ContextoDe(null);
        var bruto = await contexto.Database.SqlQuery<string>($"SELECT cpf AS \"Value\" FROM adesoes WHERE id = {adesao.Id}").SingleAsync(Ct);

        bruto.ShouldNotContain(cpf);
    }

    private static ItemDeCobrancaRequestDTO Mensalidade(long valor = 240_000) =>
        new(TipoDeCobranca.Mensalidade, null, valor, 12, 10, new DateOnly(DateTime.UtcNow.Year + 1, 3, 1));

    private async Task<Turma> TurmaPronta()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var criacao = await presidente.Cliente.PostAsJsonAsync(
            "/api/v1/cobrancas/planos",
            new PlanoDeCobrancaRequestDTO("Plano 2027", 200, 100, 0, 0),
            Json,
            Ct
        );
        var plano = (await criacao.Content.ReadFromJsonAsync<PlanoDeCobrancaDTO>(Json, Ct))!;
        (await presidente.Cliente.PostAsJsonAsync($"/api/v1/cobrancas/planos/{plano.Id}/itens", Mensalidade(), Json, Ct)).EnsureSuccessStatusCode();
        (await presidente.Cliente.PostAsync($"/api/v1/cobrancas/planos/{plano.Id}/vigorar", null, Ct)).EnsureSuccessStatusCode();
        await Publicar(presidente.Cliente, TermoV1);

        return new Turma(formaturaId, presidente, plano.Id);
    }

    private static async Task Publicar(HttpClient presidente, string conteudo) =>
        (await presidente.PostAsJsonAsync($"{Rota}/termos", new PublicarTermoRequestDTO(conteudo), Json, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.Created
        );

    private async Task<int> Parcelas(Guid formaturaId, Guid usuarioId)
    {
        var vinculoId = await VinculoDe(formaturaId, usuarioId);
        await using var contexto = fabrica.ContextoDe(formaturaId);

        return await contexto.Parcelas.CountAsync(p => p.VinculoId == vinculoId, Ct);
    }

    private async Task<Guid> VinculoDe(Guid formaturaId, Guid usuarioId)
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Vinculos.Where(v => v.FormaturaId == formaturaId && v.UsuarioId == usuarioId).Select(v => v.Id).SingleAsync(Ct);
    }
}
