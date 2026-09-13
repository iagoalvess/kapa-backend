using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Api.DTOs.Auth;
using Backend.Api.DTOs.Formaturas;
using Backend.Business.Auth.Services;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace Backend.IntegrationTests.Formaturas;

/// <summary>
/// Criação, edição, encerramento e a separação entre leitura e escrita por status.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class CriacaoDaFormaturaTests(ApiFactory fabrica)
{
    private const string Rota = "/api/v1/formaturas";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A API manda enum como texto; o cliente de teste precisa ler do mesmo jeito.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static DadosDaFormaturaRequestDTO Dados(string nome = "Medicina 2027.1 — UFPR") =>
        new(nome, "UFPR", "Medicina", DateTime.UtcNow.Year + 1, 1, new DateOnly(DateTime.UtcNow.Year + 1, 7, 15), 80);

    [Fact]
    public async Task Criar_grava_rascunho_com_vinculo_de_presidente_e_devolve_tokens_na_formatura()
    {
        // Arrange
        var cliente = fabrica.CreateClient();
        var tokensDoCadastro = await cliente.RegistrarUsuarioComum(Ct);
        var usuarioId = FormaturaDeTeste.IdDoUsuario(tokensDoCadastro.AccessToken);
        cliente.ComToken(tokensDoCadastro.AccessToken);

        // Act
        var resposta = await cliente.PostAsJsonAsync(Rota, Dados(), Ct);

        // Assert
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(Ct))!;
        var formaturaId = Guid.Parse(Claim(tokens.AccessToken, TokenService.ClaimDeFormatura)!);
        Claim(tokens.AccessToken, TokenService.ClaimDePapel).ShouldBe(PapelNaFormatura.Presidente);

        await using var contexto = fabrica.ContextoDe(null);
        var formatura = await contexto.Formaturas.SingleAsync(f => f.Id == formaturaId, Ct);
        formatura.Status.ShouldBe(StatusDaFormatura.Rascunho);
        formatura.CriadoPorUsuarioId.ShouldBe(usuarioId);
        var vinculo = await contexto.Vinculos.SingleAsync(v => v.FormaturaId == formaturaId, Ct);
        vinculo.UsuarioId.ShouldBe(usuarioId);
        vinculo.Papel.ShouldBe(PapelNaFormatura.Presidente);
    }

    [Fact]
    public async Task Segundo_rascunho_do_mesmo_usuario_devolve_409()
    {
        var cliente = fabrica.CreateClient();
        cliente.ComToken((await cliente.RegistrarUsuarioComum(Ct)).AccessToken);
        (await cliente.PostAsJsonAsync(Rota, Dados(), Ct)).EnsureSuccessStatusCode();

        var resposta = await cliente.PostAsJsonAsync(Rota, Dados("Outra turma"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("formatura.rascunho_pendente");
    }

    /// <summary>
    /// A emissão da sessão é o último passo da transação. Derrubá-lo — sem refresh token — tem de
    /// desfazer a formatura e o vínculo gravados antes dele: nenhuma linha fica.
    /// </summary>
    [Fact]
    public async Task Falha_depois_da_formatura_e_do_vinculo_nao_deixa_nenhuma_linha()
    {
        var tokens = await fabrica.CreateClient().RegistrarUsuarioComum(Ct);
        var usuarioId = FormaturaDeTeste.IdDoUsuario(tokens.AccessToken);
        var semCookie = fabrica
            .CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, BaseAddress = new Uri("https://localhost") })
            .ComToken(tokens.AccessToken);

        var resposta = await semCookie.PostAsJsonAsync(Rota, Dados(), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        await using var contexto = fabrica.ContextoDe(null);
        (await contexto.Formaturas.AnyAsync(f => f.CriadoPorUsuarioId == usuarioId, Ct)).ShouldBeFalse();
        (await contexto.Vinculos.AnyAsync(v => v.UsuarioId == usuarioId, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Dados_invalidos_devolvem_400_com_todos_os_campos()
    {
        var cliente = fabrica.CreateClient();
        cliente.ComToken((await cliente.RegistrarUsuarioComum(Ct)).AccessToken);

        var resposta = await cliente.PostAsJsonAsync(Rota, new DadosDaFormaturaRequestDTO("AB", "", "", 1999, 3, null, 0), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var corpo = await resposta.Content.ReadAsStringAsync(Ct);
        foreach (var campo in new[] { "nome", "instituicao", "curso", "ano", "semestre", "quantidadeEstimadaDeFormandos" })
            corpo.ShouldContain($"\"{campo}\"");
    }

    [Theory]
    [InlineData(PapelNaFormatura.Presidente, HttpStatusCode.OK)]
    [InlineData(PapelNaFormatura.Tesoureiro, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Comissao, HttpStatusCode.Forbidden)]
    [InlineData(PapelNaFormatura.Formando, HttpStatusCode.Forbidden)]
    public async Task Editar_dados_e_so_do_presidente(string papel, HttpStatusCode esperado)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        var resposta = await membro.Cliente.PutAsJsonAsync($"{Rota}/atual", Dados("Nome novo"), Ct);

        resposta.StatusCode.ShouldBe(esperado);
    }

    [Fact]
    public async Task Editar_grava_os_dados_novos()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(StatusDaFormatura.Rascunho, Ct), PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.PutAsJsonAsync($"{Rota}/atual", Dados("  Nome novo  "), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<FormaturaDetalheDTO>(Json, Ct))!.Nome.ShouldBe("Nome novo");
    }

    [Theory]
    [InlineData(StatusDaFormatura.Suspensa)]
    [InlineData(StatusDaFormatura.Encerrada)]
    public async Task Editar_em_modo_leitura_devolve_403_inativa(StatusDaFormatura status)
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(status, Ct), PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.PutAsJsonAsync($"{Rota}/atual", Dados(), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await resposta.Codigo(Ct)).ShouldBe("formatura.inativa");
    }

    /// <summary>
    /// A comissão se monta antes de pagar — contratar é decisão dela —, mas turma suspensa ou
    /// encerrada é leitura.
    /// </summary>
    [Theory]
    [InlineData(StatusDaFormatura.Rascunho, HttpStatusCode.NoContent)]
    [InlineData(StatusDaFormatura.AguardandoPagamento, HttpStatusCode.NoContent)]
    [InlineData(StatusDaFormatura.Suspensa, HttpStatusCode.Forbidden)]
    [InlineData(StatusDaFormatura.Encerrada, HttpStatusCode.Forbidden)]
    public async Task Montar_a_comissao_vale_antes_de_pagar_e_nao_em_modo_leitura(StatusDaFormatura status, HttpStatusCode esperado)
    {
        var formaturaId = await fabrica.CriarFormatura(status, Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var comissao = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);

        var resposta = await presidente.Cliente.DeleteAsync($"{Rota}/atual/membros/{comissao.UsuarioId}", Ct);

        resposta.StatusCode.ShouldBe(esperado);
        if (esperado == HttpStatusCode.Forbidden)
            (await resposta.Codigo(Ct)).ShouldBe("formatura.inativa");
    }

    [Fact]
    public async Task Descartar_rascunho_tira_a_turma_da_lista_de_todos()
    {
        var formaturaId = await fabrica.CriarFormatura(StatusDaFormatura.Rascunho, Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);
        var comissao = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Comissao, Ct);

        var resposta = await presidente.Cliente.PostAsync($"{Rota}/atual/descartar", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var contexto = fabrica.ContextoDe(null);
        (await contexto.Formaturas.SingleAsync(f => f.Id == formaturaId, Ct)).Status.ShouldBe(StatusDaFormatura.Descartada);
        (await contexto.Vinculos.AnyAsync(v => v.FormaturaId == formaturaId && v.Ativo, Ct)).ShouldBeFalse();
        (await comissao.Cliente.GetFromJsonAsync<List<FormaturaDoUsuarioDTO>>($"{Rota}/minhas", Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Descartar_turma_paga_devolve_409()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(StatusDaFormatura.Ativa, Ct), PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.PostAsync($"{Rota}/atual/descartar", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("formatura.transicao_invalida");
    }

    /// <summary>Suspensa é leitura, não bloqueio: o dado continua visível para todo membro.</summary>
    [Theory]
    [InlineData(PapelNaFormatura.Presidente)]
    [InlineData(PapelNaFormatura.Formando)]
    public async Task Leitura_em_formatura_suspensa_responde_200(string papel)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(StatusDaFormatura.Suspensa, Ct), papel, Ct);

        var resposta = await membro.Cliente.GetAsync($"{Rota}/atual", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<FormaturaDetalheDTO>(Json, Ct))!.Status.ShouldBe(StatusDaFormatura.Suspensa);
    }

    [Fact]
    public async Task Leitura_de_membros_em_formatura_suspensa_responde_200()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(StatusDaFormatura.Suspensa, Ct), PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.GetAsync($"{Rota}/atual/membros", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Encerrar_formatura_ativa_grava_encerrada()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var presidente = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.PostAsync($"{Rota}/atual/encerrar", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var detalhe = await presidente.Cliente.GetFromJsonAsync<FormaturaDetalheDTO>($"{Rota}/atual", Json, Ct);
        detalhe!.Status.ShouldBe(StatusDaFormatura.Encerrada);
        detalhe.EncerradaEm.ShouldNotBeNull();
    }

    [Fact]
    public async Task Encerrar_rascunho_devolve_409_transicao_invalida()
    {
        var presidente = await fabrica.NovoMembro(await fabrica.CriarFormatura(StatusDaFormatura.Rascunho, Ct), PapelNaFormatura.Presidente, Ct);

        var resposta = await presidente.Cliente.PostAsync($"{Rota}/atual/encerrar", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resposta.Codigo(Ct)).ShouldBe("formatura.transicao_invalida");
    }

    [Theory]
    [InlineData(PapelNaFormatura.Tesoureiro)]
    [InlineData(PapelNaFormatura.Comissao)]
    [InlineData(PapelNaFormatura.Formando)]
    public async Task Encerrar_e_so_do_presidente(string papel)
    {
        var membro = await fabrica.NovoMembro(await fabrica.CriarFormatura(Ct), papel, Ct);

        var resposta = await membro.Cliente.PostAsync($"{Rota}/atual/encerrar", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static string? Claim(string accessToken, string nome) =>
        new JsonWebTokenHandler().ReadJsonWebToken(accessToken).Claims.FirstOrDefault(claim => claim.Type == nome)?.Value;
}
