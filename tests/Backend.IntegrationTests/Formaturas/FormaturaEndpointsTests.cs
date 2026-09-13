using System.Net;
using System.Net.Http.Json;
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
/// Listagem, seleção e permanência da formatura na sessão.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class FormaturaEndpointsTests(ApiFactory fabrica)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Minhas_devolve_so_os_vinculos_ativos_do_usuario()
    {
        // Arrange
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        var usuarioId = IdDoUsuario(tokens.AccessToken);

        var ativa = await CriarFormaturaCom(usuarioId, PapelNaFormatura.Tesoureiro, ativo: true);
        var inativa = await CriarFormaturaCom(usuarioId, PapelNaFormatura.Formando, ativo: false);
        await CriarFormaturaCom(await OutroUsuario(), PapelNaFormatura.Presidente, ativo: true);

        // Act
        var minhas = await cliente.ComToken(tokens.AccessToken).GetFromJsonAsync<FormaturaDoUsuarioDTO[]>("/api/v1/formaturas/minhas", Ct);

        // Assert
        minhas.ShouldNotBeNull();
        minhas.Select(f => f.Id).ShouldContain(ativa);
        minhas.Select(f => f.Id).ShouldNotContain(inativa);
        minhas.Single(f => f.Id == ativa).Papel.ShouldBe(PapelNaFormatura.Tesoureiro);
        minhas.Single(f => f.Id == ativa).Semestre.ShouldBe(1);
    }

    [Fact]
    public async Task Selecionar_sem_vinculo_responde_403_com_codigo_sem_vinculo()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        var deOutro = await CriarFormaturaCom(await OutroUsuario(), PapelNaFormatura.Presidente, ativo: true);

        var resposta = await cliente.ComToken(tokens.AccessToken).PostAsync($"/api/v1/formaturas/{deOutro}/selecionar", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Codigo(resposta)).ShouldBe("formatura.sem_vinculo");
    }

    [Fact]
    public async Task Selecionar_devolve_token_com_a_formatura_e_o_papel()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        var formaturaId = await CriarFormaturaCom(IdDoUsuario(tokens.AccessToken), PapelNaFormatura.Presidente, ativo: true);

        var novos = await Selecionar(cliente.ComToken(tokens.AccessToken), formaturaId);

        Claim(novos.AccessToken, TokenService.ClaimDeFormatura).ShouldBe(formaturaId.ToString());
        Claim(novos.AccessToken, TokenService.ClaimDePapel).ShouldBe(PapelNaFormatura.Presidente);
    }

    /// <summary>
    /// O access token dura quinze minutos. Sem a formatura sobreviver à rotação, o usuário
    /// voltaria para a tela de seleção quatro vezes por hora.
    /// </summary>
    [Fact]
    public async Task Renovar_preserva_a_formatura_e_o_papel()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        var formaturaId = await CriarFormaturaCom(IdDoUsuario(tokens.AccessToken), PapelNaFormatura.Comissao, ativo: true);

        await Selecionar(cliente.ComToken(tokens.AccessToken), formaturaId);

        var resposta = await cliente.RenovarComCorpoVazio(Ct);
        resposta.EnsureSuccessStatusCode();
        var renovados = (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(Ct))!;

        Claim(renovados.AccessToken, TokenService.ClaimDeFormatura).ShouldBe(formaturaId.ToString());
        Claim(renovados.AccessToken, TokenService.ClaimDePapel).ShouldBe(PapelNaFormatura.Comissao);
    }

    /// <summary>
    /// Perder o vínculo derruba a claim na renovação seguinte, em vez de o acesso durar até o
    /// refresh token expirar.
    /// </summary>
    [Fact]
    public async Task Renovar_derruba_a_formatura_quando_o_vinculo_e_desativado()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        var usuarioId = IdDoUsuario(tokens.AccessToken);
        var formaturaId = await CriarFormaturaCom(usuarioId, PapelNaFormatura.Formando, ativo: true);

        await Selecionar(cliente.ComToken(tokens.AccessToken), formaturaId);
        await DesativarVinculo(usuarioId, formaturaId);

        var resposta = await cliente.RenovarComCorpoVazio(Ct);
        resposta.EnsureSuccessStatusCode();
        var renovados = (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(Ct))!;

        Claim(renovados.AccessToken, TokenService.ClaimDeFormatura).ShouldBeNull();
    }

    /// <summary>
    /// Sem formatura, barrado numa política que nada tem a ver com formatura, o código é
    /// <c>auth.sem_permissao</c>: mandar o usuário para a seleção só trocaria um 403 por outro.
    /// O caso <c>formatura.nao_selecionada</c> é coberto em <c>RespostaDeAutorizacaoTests</c>.
    /// </summary>
    [Fact]
    public async Task Sem_formatura_a_politica_de_administrador_responde_sem_permissao()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);

        var resposta = await cliente.ComToken(tokens.AccessToken).GetAsync("/api/v1/admin/resumo", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Codigo(resposta)).ShouldBe("auth.sem_permissao");
    }

    [Fact]
    public async Task Selecionar_com_vinculo_inativo_responde_403_com_codigo_sem_vinculo()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        var formaturaId = await CriarFormaturaCom(IdDoUsuario(tokens.AccessToken), PapelNaFormatura.Formando, ativo: false);

        var resposta = await cliente.ComToken(tokens.AccessToken).PostAsync($"/api/v1/formaturas/{formaturaId}/selecionar", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Codigo(resposta)).ShouldBe("formatura.sem_vinculo");
    }

    /// <summary>
    /// Trocar de formatura é rotação: o refresh token de antes não pode continuar valendo, ou
    /// cada troca deixaria para trás uma sessão que nem o logout alcança.
    /// </summary>
    [Fact]
    public async Task Selecionar_revoga_o_refresh_token_anterior()
    {
        var cliente = fabrica.CreateClient();
        var (tokens, refreshAnterior) = await cliente.RegistrarCapturandoCookie(Ct);
        var formaturaId = await CriarFormaturaCom(IdDoUsuario(tokens.AccessToken), PapelNaFormatura.Formando, ativo: true);

        await Selecionar(cliente.ComToken(tokens.AccessToken), formaturaId);

        var resposta = await fabrica.RenovarComCookie(refreshAnterior, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Só o access token não basta: senão ele viraria uma fábrica de refresh tokens, e quem o
    /// levasse por um XSS trocaria quinze minutos de acesso por dias de sessão.
    /// </summary>
    [Fact]
    public async Task Selecionar_sem_o_refresh_token_atual_responde_401()
    {
        var tokens = await fabrica.CreateClient().RegistrarUsuarioComum(Ct);
        var formaturaId = await CriarFormaturaCom(IdDoUsuario(tokens.AccessToken), PapelNaFormatura.Formando, ativo: true);

        var semCookie = fabrica
            .CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, BaseAddress = new Uri("https://localhost") })
            .ComToken(tokens.AccessToken);

        var resposta = await semCookie.PostAsync($"/api/v1/formaturas/{formaturaId}/selecionar", null, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        resposta.RefreshTokenDoCookie().ShouldBeNull();
    }

    [Fact]
    public async Task Com_formatura_selecionada_a_falta_de_permissao_volta_a_ser_sem_permissao()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        var formaturaId = await CriarFormaturaCom(IdDoUsuario(tokens.AccessToken), PapelNaFormatura.Presidente, ativo: true);

        var novos = await Selecionar(cliente.ComToken(tokens.AccessToken), formaturaId);

        var resposta = await cliente.ComToken(novos.AccessToken).GetAsync("/api/v1/admin/resumo", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Codigo(resposta)).ShouldBe("auth.sem_permissao");
    }

    /// <summary>
    /// Vínculo único entra na sessão sozinho — a tela de seleção não decidiria nada.
    /// </summary>
    [Fact]
    public async Task Login_com_um_vinculo_ativo_ja_traz_a_formatura_no_token()
    {
        var cliente = fabrica.CreateClient();
        var email = $"unico-{Guid.CreateVersion7():N}@testes.local";
        var usuarioId = IdDoUsuario((await cliente.RegistrarComEmail(email, Ct)).AccessToken);

        var formaturaId = await CriarFormaturaCom(usuarioId, PapelNaFormatura.Presidente, ativo: true);

        var tokens = await cliente.AutenticarCom(email, Ct);

        Claim(tokens.AccessToken, TokenService.ClaimDeFormatura).ShouldBe(formaturaId.ToString());
        Claim(tokens.AccessToken, TokenService.ClaimDePapel).ShouldBe(PapelNaFormatura.Presidente);
    }

    /// <summary>
    /// Com duas, escolher no lugar do usuário seria chutar: o token sai sem a claim e a guarda
    /// leva para a seleção.
    /// </summary>
    [Fact]
    public async Task Login_com_dois_vinculos_nao_escolhe_por_conta_propria()
    {
        var cliente = fabrica.CreateClient();
        var email = $"duplo-{Guid.CreateVersion7():N}@testes.local";
        var usuarioId = IdDoUsuario((await cliente.RegistrarComEmail(email, Ct)).AccessToken);

        await CriarFormaturaCom(usuarioId, PapelNaFormatura.Presidente, ativo: true);
        await CriarFormaturaCom(usuarioId, PapelNaFormatura.Formando, ativo: true);

        var tokens = await cliente.AutenticarCom(email, Ct);

        Claim(tokens.AccessToken, TokenService.ClaimDeFormatura).ShouldBeNull();
    }

    /// <summary>
    /// Vínculo desativado não conta: quem perdeu o único acesso volta para o onboarding, e não
    /// para uma turma que não é mais dele.
    /// </summary>
    [Fact]
    public async Task Login_ignora_vinculo_inativo_na_escolha_automatica()
    {
        var cliente = fabrica.CreateClient();
        var email = $"inativo-{Guid.CreateVersion7():N}@testes.local";
        var usuarioId = IdDoUsuario((await cliente.RegistrarComEmail(email, Ct)).AccessToken);

        await CriarFormaturaCom(usuarioId, PapelNaFormatura.Formando, ativo: false);

        var tokens = await cliente.AutenticarCom(email, Ct);

        Claim(tokens.AccessToken, TokenService.ClaimDeFormatura).ShouldBeNull();
    }

    private static async Task<TokenResponseDTO> Selecionar(HttpClient cliente, Guid formaturaId)
    {
        var resposta = await cliente.PostAsync($"/api/v1/formaturas/{formaturaId}/selecionar", null, Ct);

        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(Ct))!;
    }

    /// <summary>
    /// Registra uma conta qualquer para ser dona de uma formatura alheia.
    /// </summary>
    /// <remarks>
    /// Um <c>Guid</c> inventado não serve: o vínculo tem chave estrangeira para o usuário, e o
    /// insert seria recusado pelo banco antes de o teste chegar ao que ele quer provar.
    /// </remarks>
    private async Task<Guid> OutroUsuario() => IdDoUsuario((await fabrica.CreateClient().RegistrarUsuarioComum(Ct)).AccessToken);

    private async Task<Guid> CriarFormaturaCom(Guid usuarioId, string papel, bool ativo)
    {
        await using var contexto = fabrica.ContextoDe(null);

        var formatura = FormaturaDeTeste.NovaFormatura();

        contexto.Formaturas.Add(formatura);
        contexto.Vinculos.Add(
            new VinculoDeFormatura
            {
                UsuarioId = usuarioId,
                FormaturaId = formatura.Id,
                Papel = papel,
                Ativo = ativo,
            }
        );

        await contexto.SaveChangesAsync(Ct);

        return formatura.Id;
    }

    private async Task DesativarVinculo(Guid usuarioId, Guid formaturaId)
    {
        await using var contexto = fabrica.ContextoDe(null);

        var vinculo = await contexto.Vinculos.SingleAsync(v => v.UsuarioId == usuarioId && v.FormaturaId == formaturaId, Ct);
        vinculo.Ativo = false;

        await contexto.SaveChangesAsync(Ct);
    }

    private static async Task<string?> Codigo(HttpResponseMessage resposta)
    {
        var problema = await resposta.Content.ReadFromJsonAsync<Dictionary<string, object>>(Ct);

        return problema?.GetValueOrDefault("codigo")?.ToString();
    }

    private static Guid IdDoUsuario(string accessToken) => Guid.Parse(Claim(accessToken, JwtRegisteredClaimNames.Sub)!);

    private static string? Claim(string accessToken, string nome) =>
        new JsonWebTokenHandler().ReadJsonWebToken(accessToken).Claims.FirstOrDefault(claim => claim.Type == nome)?.Value;
}
