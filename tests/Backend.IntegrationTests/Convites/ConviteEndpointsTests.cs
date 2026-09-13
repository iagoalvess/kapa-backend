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

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Criar_e_listar_seguem_a_politica_de_gestao(string papel, HttpStatusCode esperado)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        var criacao = await membro.Cliente.PostAsJsonAsync(Gestao, new CriarConviteRequestDTO(null, null, null, null), Ct);
        var listagem = await membro.Cliente.GetAsync(Gestao, Ct);

        criacao.StatusCode.ShouldBe(esperado);
        listagem.StatusCode.ShouldBe(esperado);
    }

    [Fact]
    public async Task Comissao_convidando_para_tesoureiro_recebe_403()
    {
        var comissao = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Comissao, Ct);

        var resposta = await comissao.Cliente.PostAsJsonAsync(
            Gestao,
            new CriarConviteRequestDTO("ana@exemplo.com", PapelNaFormatura.Tesoureiro, null, null),
            Ct
        );

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await resposta.Codigo(Ct)).ShouldBe("convite.papel_restrito");
    }

    /// <summary>Antes de pagar, o Presidente monta a comissão; formando só entra com a turma ativa.</summary>
    [Fact]
    public async Task Rascunho_convida_a_comissao_mas_nao_formandos()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(StatusDaFormatura.Rascunho, Ct), PapelNaFormatura.Presidente, Ct);

        var formando = await presidente.Cliente.PostAsJsonAsync(Gestao, new CriarConviteRequestDTO(null, null, null, null), Ct);
        var tesoureiro = await presidente.Cliente.PostAsJsonAsync(
            Gestao,
            new CriarConviteRequestDTO("tesoureiro@testes.local", PapelNaFormatura.Tesoureiro, null, null),
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
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Rascunho, Ct);
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

    [Fact]
    public async Task Turma_suspensa_nao_cria_convite()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(StatusDaFormatura.Suspensa, Ct), PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.PostAsJsonAsync(Gestao, new CriarConviteRequestDTO(null, null, null, null), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await resposta.Codigo(Ct)).ShouldBe("formatura.inativa");
    }

    [Fact]
    public async Task O_banco_guarda_so_o_sha256_do_token()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);

        var criado = await Criar(presidente, new CriarConviteRequestDTO("fulano@testes.local", null, null, null));
        var token = TokenDo(criado);

        await using var contexto = fabrica.ContextoDe(null);
        var hash = await contexto.Convites.IgnoreQueryFilters().Where(c => c.Id == criado.Id).Select(c => c.TokenHash).SingleAsync(Ct);
        hash.ShouldBe(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))));
        (await contexto.Convites.IgnoreQueryFilters().AnyAsync(c => c.TokenHash == token, Ct)).ShouldBeFalse();
        (await contexto.EmailsFila.AnyAsync(e => e.Para == "fulano@testes.local" && e.CorpoHtml.Contains(token), Ct)).ShouldBeTrue();
    }

    /// <summary>A URL circula em grupo de WhatsApp: turma, instituição e papel, e mais nada.</summary>
    [Fact]
    public async Task Consulta_anonima_devolve_so_turma_instituicao_e_papel()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var token = TokenDo(await Criar(presidente, new CriarConviteRequestDTO(null, null, null, null)));

        var resposta = await fabrica.CreateClient().GetAsync($"{Publico}/{token}", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Ct);
        corpo.EnumerateObject().Select(p => p.Name).ShouldBe(["turma", "instituicao", "papel"], ignoreOrder: true);
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
                var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Ct);
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
        var sessao = (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(Ct))!;
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
        var criado = await Criar(presidente, new CriarConviteRequestDTO(null, null, null, null));

        (await presidente.Cliente.DeleteAsync($"{Gestao}/{criado.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await fabrica.CreateClient().GetAsync($"{Publico}/{TokenDo(criado)}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var lista = await presidente.Cliente.GetFromJsonAsync<List<ConviteResumoDTO>>(Gestao, Json, Ct);
        lista!.ShouldHaveSingleItem().Status.ShouldBe(StatusDoConvite.Revogado);
    }

    /// <summary>A formatura vem do token: convite de outra turma não existe aqui.</summary>
    [Fact]
    public async Task Revogar_convite_de_outra_formatura_responde_404()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var deOutra = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), PapelNaFormatura.Presidente, Ct);
        var alheio = await Criar(deOutra, new CriarConviteRequestDTO(null, null, null, null));

        var resposta = await presidente.Cliente.DeleteAsync($"{Gestao}/{alheio.Id}", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await presidente.Cliente.GetFromJsonAsync<List<ConviteResumoDTO>>(Gestao, Json, Ct))!.ShouldBeEmpty();
    }

    private static async Task<ConviteCriadoDTO> Criar(MembroDeTeste membro, CriarConviteRequestDTO pedido)
    {
        var resposta = await membro.Cliente.PostAsJsonAsync(Gestao, pedido, Ct);
        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<ConviteCriadoDTO>(Ct))!;
    }

    private static string TokenDo(ConviteCriadoDTO criado) => criado.Link.Split('/').Last();

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
