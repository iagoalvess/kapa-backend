using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Api.DTOs.Auth;
using Backend.Api.DTOs.Convites;
using Backend.Business.Convites.Models;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace Backend.IntegrationTests.Convites;

/// <summary>
/// Convites contra a API e o banco de verdade: quem cria, o que o banco guarda, o que o anônimo
/// vê e o que o aceite faz — inclusive com dois cliques no mesmo segundo.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class ConviteEndpointsTests(ApiFactory fabrica)
{
    private const string Gestao = "/api/v1/formaturas/atual/convites";
    private const string Publico = "/api/v1/convites";

    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Criar_e_listar_seguem_a_politica_de_gestao(string papel, HttpStatusCode esperado)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        var criacao = await membro.Cliente.PostAsJsonAsync(Gestao, new CriarConviteRequestDTO(null, null), Ct);
        var listagem = await membro.Cliente.GetAsync(Gestao, Ct);

        criacao.StatusCode.ShouldBe(esperado);
        listagem.StatusCode.ShouldBe(esperado);
    }

    [Fact]
    public async Task Comissao_convidando_para_tesoureiro_recebe_403()
    {
        var comissao = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Comissao, Ct);

        var resposta = await comissao.Cliente.PostAsJsonAsync(Gestao, new CriarConviteRequestDTO("ana@exemplo.com", PapelNaFormatura.Tesoureiro), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await resposta.Codigo(Ct)).ShouldBe("convite.papel_restrito");
    }

    /// <summary>No gratuito o Presidente monta a comissão; formando só entra depois de contratar.</summary>
    [Fact]
    public async Task No_gratuito_convida_a_comissao_mas_nao_formandos()
    {
        var presidente = await fabrica.NovoMembro(
            await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct, contratada: false),
            PapelNaFormatura.Presidente,
            Ct
        );

        var formando = await presidente.Cliente.PostAsJsonAsync(Gestao, new CriarConviteRequestDTO(null, null), Ct);
        var tesoureiro = await presidente.Cliente.PostAsJsonAsync(
            Gestao,
            new CriarConviteRequestDTO("tesoureiro@testes.local", PapelNaFormatura.Tesoureiro),
            Json,
            Ct
        );

        formando.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await formando.Codigo(Ct)).ShouldBe("convite.formatura_nao_contratada");
        tesoureiro.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Comissao_convidada_antes_do_pagamento_entra_no_rascunho()
    {
        var email = $"tesoureira-{Guid.CreateVersion7():N}@testes.local";
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct);
        var token = await Semear(
            formaturaId,
            c =>
            {
                c.Email = email;
                c.Papel = PapelNaFormatura.Tesoureiro;
            }
        );
        var cliente = fabrica.CreateClient();
        cliente.ComToken((await cliente.RegistrarComEmail(email, Ct)).AccessToken);
        await fabrica.ConfirmarEmail(email, Ct);

        (await Aceitar(cliente, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Desligado não volta por convite: voltaria sem as parcelas que o desligamento cancelou — o
    /// mesmo motivo que tirou o religar (23/09/2026).
    /// </summary>
    [Fact]
    public async Task Desligado_nao_volta_por_convite_pessoal()
    {
        var email = $"desligada-{Guid.CreateVersion7():N}@testes.local";
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct);
        var cliente = fabrica.CreateClient();
        cliente.ComToken((await cliente.RegistrarComEmail(email, Ct)).AccessToken);
        await fabrica.ConfirmarEmail(email, Ct);

        await using (var contexto = fabrica.ContextoDe(formaturaId))
        {
            var usuario = await contexto.Users.SingleAsync(u => u.Email == email, Ct);
            var vinculo = new VinculoDeFormatura
            {
                UsuarioId = usuario.Id,
                FormaturaId = formaturaId,
                Papel = PapelNaFormatura.Formando,
            };
            vinculo.Desligar(MotivoDeSaida.Trancamento, null, DateTime.UtcNow);
            contexto.Vinculos.Add(vinculo);
            await contexto.SaveChangesAsync(Ct);
        }

        var token = await Semear(formaturaId, c => c.Email = email);

        var resposta = await Aceitar(cliente, token);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await resposta.Codigo(Ct)).ShouldBe("convite.membro_desligado");
    }

    [Fact]
    public async Task Turma_suspensa_nao_cria_convite()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(StatusDaFormatura.Suspensa, Ct), PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.PostAsJsonAsync(Gestao, new CriarConviteRequestDTO(null, null), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await resposta.Codigo(Ct)).ShouldBe("formatura.inativa");
    }

    [Fact]
    public async Task O_banco_guarda_so_o_sha256_do_token_do_nominal()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        var criado = await Criar(presidente, new CriarConviteRequestDTO("fulano@testes.local", null));
        var token = TokenDo(criado);

        await using var contexto = fabrica.ContextoDe(null);
        var hash = await contexto.Convites.IgnoreQueryFilters().Where(c => c.Id == criado.Id).Select(c => c.TokenHash).SingleAsync(Ct);
        hash.ShouldBe(Hash(token));
        (await contexto.Convites.IgnoreQueryFilters().AnyAsync(c => c.TokenHash == token || c.Token == token, Ct)).ShouldBeFalse();
        (await contexto.EmailsFila.AnyAsync(e => e.Para == "fulano@testes.local" && e.CorpoHtml.Contains(token), Ct)).ShouldBeTrue();
    }

    /// <summary>A URL circula em grupo de WhatsApp: turma, instituição e papel, e mais nada.</summary>
    [Fact]
    public async Task Consulta_anonima_devolve_so_turma_instituicao_e_papel()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var token = TokenDo(await Criar(presidente, new CriarConviteRequestDTO(null, null)));

        var resposta = await fabrica.CreateClient().GetAsync($"{Publico}/{token}", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);

        // O link da turma não é nominal, então não há e-mail a mascarar. O campo aparece como `null`
        // — a API escreve o nulo em vez de omitir o campo —, e o que importa é que ele venha vazio:
        // é o e-mail do convidado que não pode escapar por um endpoint anônimo.
        corpo.EnumerateObject().Select(p => p.Name).ShouldBe(["turma", "instituicao", "papel", "email_mascarado"], ignoreOrder: true);
        corpo.GetProperty("email_mascarado").ValueKind.ShouldBe(JsonValueKind.Null);
        corpo.GetProperty("papel").GetString().ShouldBe(PapelNaFormatura.Formando);
    }

    /// <summary>Diferenciar os quatro casos transformaria o endpoint num oráculo de tokens.</summary>
    [Fact]
    public async Task Inexistente_expirado_revogado_e_esgotado_devolvem_a_mesma_resposta()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var expirado = await Semear(formaturaId, c => c.ExpiraEm = DateTime.UtcNow.AddMinutes(-1));
        var revogado = await Semear(formaturaId, c => c.Revogar(DateTime.UtcNow));
        var esgotado = await Semear(
            formaturaId,
            c =>
            {
                c.UsosMaximos = 2;
                c.UsosFeitos = 2;
            }
        );
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        cliente.ComToken(tokens.AccessToken);

        foreach (var token in new[] { "nao-existe", expirado, revogado, esgotado })
        {
            foreach (var resposta in new[] { await cliente.GetAsync($"{Publico}/{token}", Ct), await Aceitar(cliente, token) })
            {
                resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
                var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
                corpo.GetProperty("codigo").GetString().ShouldBe("convite.invalido");
                corpo.GetProperty("title").GetString().ShouldBe("Este convite não está mais disponível. Peça um novo à comissão.");
            }
        }
    }

    /// <summary>
    /// Dois aceites no mesmo instante de um convite de um uso só: um entra, o outro recebe 409.
    /// Repetido para a corrida acontecer de verdade.
    /// </summary>
    [Fact]
    public async Task Aceites_simultaneos_de_um_uso_so_geram_um_vinculo()
    {
        for (var rodada = 0; rodada < 5; rodada++)
        {
            var formaturaId = await fabrica.CriarFormatura(Ct);
            var token = await Semear(formaturaId, c => c.UsosMaximos = 1);
            var a = await ClienteNovo();
            var b = await ClienteNovo();

            var respostas = await Task.WhenAll(Aceitar(a, token), Aceitar(b, token));

            respostas.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);

            await using var contexto = fabrica.ContextoDe(null);
            (await contexto.Vinculos.CountAsync(v => v.FormaturaId == formaturaId, Ct)).ShouldBe(1);
            (await contexto.Convites.IgnoreQueryFilters().SingleAsync(c => c.FormaturaId == formaturaId, Ct)).UsosFeitos.ShouldBe(1);
        }
    }

    /// <summary>O corpo do aceite não escolhe papel: quem tenta virar tesoureiro pelo DevTools entra como formando.</summary>
    [Fact]
    public async Task Aceite_usa_o_papel_do_convite_e_ignora_o_do_corpo()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var token = await Semear(formaturaId, _ => { });
        var cliente = await ClienteNovo();

        var resposta = await cliente.PostAsync(
            $"{Publico}/{token}/aceitar",
            new StringContent("""{"papel":"Tesoureiro"}""", Encoding.UTF8, "application/json"),
            Ct
        );

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var contexto = fabrica.ContextoDe(null);
        (await contexto.Vinculos.SingleAsync(v => v.FormaturaId == formaturaId, Ct)).Papel.ShouldBe(PapelNaFormatura.Formando);
    }

    /// <summary>O usuário cai dentro da turma: o token já traz a formatura e o papel.</summary>
    [Fact]
    public async Task Aceite_devolve_sessao_com_formatura_e_papel_e_registra_o_aceite()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var token = await Semear(formaturaId, c => c.Papel = PapelNaFormatura.Comissao);
        var cliente = await ClienteNovo();

        var resposta = await Aceitar(cliente, token);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var sessao = (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(Json, Ct))!;
        var claims = new JsonWebTokenHandler().ReadJsonWebToken(sessao.AccessToken).Claims.ToList();
        claims.Single(c => c.Type == "formatura_id").Value.ShouldBe(formaturaId.ToString());
        claims.Single(c => c.Type == "papel").Value.ShouldBe(PapelNaFormatura.Comissao);

        cliente.ComToken(sessao.AccessToken);
        (await cliente.GetAsync("/api/v1/formaturas/atual", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var contexto = fabrica.ContextoDe(null);
        (await contexto.AceitesDeConvite.CountAsync(a => a.UsuarioId == FormaturaDeTeste.IdDoUsuario(sessao.AccessToken), Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Aceitar_duas_vezes_devolve_409_sem_vinculo_duplicado()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var token = await Semear(formaturaId, _ => { });
        var cliente = await ClienteNovo();

        (await Aceitar(cliente, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var segunda = await Aceitar(cliente, token);

        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await segunda.Codigo(Ct)).ShouldBe("convite.ja_vinculado");
        await using var contexto = fabrica.ContextoDe(null);
        (await contexto.Vinculos.CountAsync(v => v.FormaturaId == formaturaId, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Nominal_aceito_por_outro_email_devolve_403()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var token = await Semear(formaturaId, c => c.Email = "convidado@testes.local");
        var cliente = await ClienteNovo();

        var resposta = await Aceitar(cliente, token);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await resposta.Codigo(Ct)).ShouldBe("convite.email_divergente");
    }

    [Fact]
    public async Task Nominal_aceito_pelo_email_convidado_entra()
    {
        var email = $"convidado-{Guid.CreateVersion7():N}@testes.local";
        var token = await Semear(await fabrica.CriarFormatura(Ct), c => c.Email = email.ToUpperInvariant());
        var cliente = fabrica.CreateClient();
        cliente.ComToken((await cliente.RegistrarComEmail(email, Ct)).AccessToken);
        await fabrica.ConfirmarEmail(email, Ct);

        (await Aceitar(cliente, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Quem recebeu o link encaminhado cria conta com o e-mail convidado, mas não o confirma.</summary>
    [Fact]
    public async Task Nominal_sem_email_confirmado_devolve_403()
    {
        var email = $"convidado-{Guid.CreateVersion7():N}@testes.local";
        var token = await Semear(await fabrica.CriarFormatura(Ct), c => c.Email = email);
        var cliente = fabrica.CreateClient();
        cliente.ComToken((await cliente.RegistrarComEmail(email, Ct)).AccessToken);

        var resposta = await Aceitar(cliente, token);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await resposta.Codigo(Ct)).ShouldBe("convite.email_nao_confirmado");
    }

    [Fact]
    public async Task Revogar_derruba_o_link_na_hora()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var criado = await Criar(presidente, new CriarConviteRequestDTO(null, null));

        (await presidente.Cliente.DeleteAsync($"{Gestao}/{criado.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await fabrica.CreateClient().GetAsync($"{Publico}/{TokenDo(criado)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var lista = await presidente.Cliente.GetFromJsonAsync<List<ConviteResumoDTO>>(Gestao, Json, Ct);
        lista!.ShouldHaveSingleItem().Status.ShouldBe(StatusDoConvite.Revogado);
    }

    /// <summary>Um link por turma: o novo derruba o anterior, e a listagem devolve só o vigente para copiar.</summary>
    [Fact]
    public async Task Link_novo_revoga_o_anterior_e_a_listagem_mostra_o_vigente()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var primeiro = await Criar(presidente, new CriarConviteRequestDTO(null, null));
        var nominal = await Criar(presidente, new CriarConviteRequestDTO("fulano@testes.local", null));

        var segundo = await Criar(presidente, new CriarConviteRequestDTO(null, null));

        (await fabrica.CreateClient().GetAsync($"{Publico}/{TokenDo(primeiro)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var lista = (await presidente.Cliente.GetFromJsonAsync<List<ConviteResumoDTO>>(Gestao, Json, Ct))!;
        lista.Single(c => c.Id == segundo.Id).Link.ShouldBe(segundo.Link);
        lista
            .Single(c => c.Id == primeiro.Id)
            .ShouldSatisfyAllConditions(c => c.Status.ShouldBe(StatusDoConvite.Revogado), c => c.Link.ShouldBeNull());
        lista
            .Single(c => c.Id == nominal.Id)
            .ShouldSatisfyAllConditions(c => c.Status.ShouldBe(StatusDoConvite.Pendente), c => c.Link.ShouldBeNull());
    }

    /// <summary>A formatura vem do token: convite de outra turma não existe aqui.</summary>
    [Fact]
    public async Task Revogar_convite_de_outra_formatura_responde_404()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var deOutra = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var alheio = await Criar(deOutra, new CriarConviteRequestDTO(null, null));

        var resposta = await presidente.Cliente.DeleteAsync($"{Gestao}/{alheio.Id}", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await presidente.Cliente.GetFromJsonAsync<List<ConviteResumoDTO>>(Gestao, Json, Ct))!.ShouldBeEmpty();
    }

    /// <summary>Cria pela API e acha o id pelo hash do token: a resposta da criação só traz o link.</summary>
    private async Task<CriadoNaApi> Criar(MembroDeTeste membro, CriarConviteRequestDTO pedido)
    {
        var resposta = await membro.Cliente.PostAsJsonAsync(Gestao, pedido, Json, Ct);
        resposta.EnsureSuccessStatusCode();

        var link = (await resposta.Content.ReadFromJsonAsync<ConviteCriadoDTO>(Json, Ct))!.Link;
        var hash = Hash(link.Split('/').Last());

        await using var contexto = fabrica.ContextoDe(null);
        var id = await contexto.Convites.IgnoreQueryFilters().Where(c => c.TokenHash == hash).Select(c => c.Id).SingleAsync(Ct);

        return new CriadoNaApi(id, link);
    }

    private static string TokenDo(CriadoNaApi criado) => criado.Link.Split('/').Last();

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>O convite criado, com o id que a resposta não traz.</summary>
    private sealed record CriadoNaApi(Guid Id, string Link);

    private static Task<HttpResponseMessage> Aceitar(HttpClient cliente, string token) =>
        cliente.PostAsync($"{Publico}/{token}/aceitar", new StringContent("{}", Encoding.UTF8, "application/json"), Ct);

    /// <summary>Conta nova, autenticada e com o cookie de sessão — o aceite rotaciona o refresh token.</summary>
    private async Task<HttpClient> ClienteNovo()
    {
        var cliente = fabrica.CreateClient();

        return cliente.ComToken((await cliente.RegistrarUsuarioComum(Ct)).AccessToken);
    }

    /// <summary>
    /// Grava um convite direto no banco e devolve o token puro.
    /// </summary>
    /// <remarks>Direto no banco para montar qualquer estado — expirado, esgotado — sem esperar o relógio.</remarks>
    private async Task<string> Semear(Guid formaturaId, Action<Convite> ajustar)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var convite = new Convite
        {
            TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
            ExpiraEm = DateTime.UtcNow.AddDays(1),
            CriadoPorUsuarioId = await UsuarioQualquer(),
        };
        ajustar(convite);

        await using var contexto = fabrica.ContextoDe(formaturaId);
        contexto.Convites.Add(convite);
        await contexto.SaveChangesAsync(Ct);

        return token;
    }

    private async Task<Guid> UsuarioQualquer()
    {
        await using var contexto = fabrica.ContextoDe(null);

        return await contexto.Users.Select(u => u.Id).FirstAsync(Ct);
    }
}
