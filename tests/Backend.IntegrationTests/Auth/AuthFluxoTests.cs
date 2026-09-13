using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Auth;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Mvc;
using Shouldly;

namespace Backend.IntegrationTests.Auth;

/// <summary>
/// Percorre o ciclo de sessão contra a API e o banco reais.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class AuthFluxoTests(ApiFactory fabrica)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registrar_cria_a_conta_e_ja_devolve_a_sessao()
    {
        var (tokens, refresh) = await fabrica.CreateClient().RegistrarCapturandoCookie(Ct);

        tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
        tokens.ExpiraEm.ShouldBeGreaterThan(DateTime.UtcNow);

        // O refresh token existe, mas só no cookie — nunca no corpo.
        tokens.RefreshToken.ShouldBeNull();
        refresh.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Registrar_com_email_ja_usado_devolve_409()
    {
        var cliente = fabrica.CreateClient();
        var email = $"repetido-{Guid.CreateVersion7():N}@testes.local";
        var corpo = await cliente.CorpoDeCadastro("Primeiro", email, "Senha@Teste123", Ct);

        (await cliente.PostAsJsonAsync("/api/v1/auth/registrar", corpo, Ct)).EnsureSuccessStatusCode();

        var segunda = await cliente.PostAsJsonAsync("/api/v1/auth/registrar", corpo, Ct);

        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Registrar_com_senha_fraca_devolve_400_com_o_motivo_no_campo_senha()
    {
        var cliente = fabrica.CreateClient();
        var corpo = await cliente.CorpoDeCadastro("Fraco", $"fraco-{Guid.CreateVersion7():N}@testes.local", "123", Ct);

        var resposta = await cliente.PostAsJsonAsync("/api/v1/auth/registrar", corpo, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problema = await resposta.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);
        problema!.Errors.ShouldContainKey("senha");
    }

    [Fact]
    public async Task Login_com_senha_errada_devolve_401()
    {
        var resposta = await fabrica
            .CreateClient()
            .PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDTO(ApiFactory.AdminEmail, "SenhaErrada@123"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Impede que o login vire um verificador de quais e-mails têm conta.
    /// </summary>
    /// <remarks>
    /// A comparação ignora <c>traceId</c> e <c>instance</c>, que mudam a cada requisição por
    /// definição. O que precisa ser idêntico é o par status + código de erro — é isso que um
    /// atacante consegue observar para distinguir os dois casos.
    /// </remarks>
    [Fact]
    public async Task Login_de_conta_inexistente_responde_igual_a_senha_errada()
    {
        var cliente = fabrica.CreateClient();

        var inexistente = await cliente.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDTO("ninguem@testes.local", "Qualquer@123"), Ct);
        var senhaErrada = await cliente.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDTO(ApiFactory.AdminEmail, "Errada@123"), Ct);

        inexistente.StatusCode.ShouldBe(senhaErrada.StatusCode);

        var problemaInexistente = await inexistente.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var problemaSenhaErrada = await senhaErrada.Content.ReadFromJsonAsync<JsonElement>(Ct);

        problemaInexistente.GetProperty("codigo").GetString().ShouldBe(problemaSenhaErrada.GetProperty("codigo").GetString());
        problemaInexistente.GetProperty("title").GetString().ShouldBe(problemaSenhaErrada.GetProperty("title").GetString());
    }

    /// <summary>
    /// "Conta bloqueada" também diria ao atacante que o e-mail tem conta: depois de cinco senhas
    /// erradas, até a senha certa responde como credencial inválida.
    /// </summary>
    [Fact]
    public async Task Conta_bloqueada_responde_igual_a_credencial_invalida()
    {
        var cliente = fabrica.CreateClient();
        var email = $"bloqueada-{Guid.CreateVersion7():N}@testes.local";
        var cadastro = await cliente.CorpoDeCadastro("Bloqueada", email, "Senha@Teste123", Ct);
        (await cliente.PostAsJsonAsync("/api/v1/auth/registrar", cadastro, Ct)).EnsureSuccessStatusCode();

        for (var tentativa = 0; tentativa < 5; tentativa++)
            await cliente.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDTO(email, "Errada@123"), Ct);

        var comSenhaCerta = await cliente.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDTO(email, "Senha@Teste123"), Ct);

        comSenhaCerta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await comSenhaCerta.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("codigo").GetString().ShouldBe("auth.credenciais_invalidas");
    }

    /// <summary>
    /// Corpo sem os campos chega ao validador do <c>Business</c>, e não ao <c>[Required]</c>
    /// implícito do MVC — que respondia em inglês e sem <c>codigo</c>.
    /// </summary>
    [Fact]
    public async Task Corpo_vazio_devolve_400_com_codigo_de_validacao()
    {
        var resposta = await fabrica.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { }, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var codigo = (await resposta.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("codigo").GetString() ?? "";
        codigo.ShouldContain('.');
    }

    /// <summary>
    /// O refresh token sai em cookie <c>HttpOnly</c> e <b>não</b> no corpo.
    /// </summary>
    /// <remarks>
    /// As duas metades importam igualmente. O <c>HttpOnly</c> tira o token do alcance do
    /// JavaScript; mantê-lo fora do corpo é o que impede um XSS de simplesmente chamar
    /// <c>/auth/refresh</c> e ler o token novo da resposta, o que anularia o cookie.
    /// </remarks>
    [Fact]
    public async Task O_refresh_token_sai_em_cookie_httponly_e_nunca_no_corpo()
    {
        var cliente = fabrica.CreateClient();

        var resposta = await cliente.PostAsJsonAsync(
            "/api/v1/auth/registrar",
            await cliente.CorpoDeCadastro("Cookie", $"cookie-{Guid.CreateVersion7():N}@testes.local", "Senha@Teste123", Ct),
            Ct
        );

        resposta.EnsureSuccessStatusCode();

        var corpo = (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(Ct))!;
        corpo.AccessToken.ShouldNotBeNullOrWhiteSpace();
        corpo.RefreshToken.ShouldBeNull();

        var setCookie = resposta.Headers.GetValues("Set-Cookie").Single(valor => valor.StartsWith("refresh_token=", StringComparison.Ordinal));

        setCookie.ShouldContain("httponly", Case.Insensitive);
        setCookie.ShouldContain("samesite=lax", Case.Insensitive);
        setCookie.ShouldContain("path=/api", Case.Insensitive);
    }

    [Fact]
    public async Task Renovar_devolve_um_par_novo_e_invalida_o_anterior()
    {
        var cliente = fabrica.CreateClient();
        var (_, refreshOriginal) = await cliente.RegistrarCapturandoCookie(Ct);

        var renovacao = await cliente.RenovarComCorpoVazio(Ct);
        renovacao.EnsureSuccessStatusCode();

        renovacao.RefreshTokenDoCookie().ShouldNotBe(refreshOriginal);

        var reuso = await fabrica.RenovarComCookie(refreshOriginal, Ct);
        reuso.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reusar_um_refresh_token_rotacionado_derruba_todas_as_sessoes()
    {
        var cliente = fabrica.CreateClient();
        var (_, refreshOriginal) = await cliente.RegistrarCapturandoCookie(Ct);

        var renovacao = await cliente.RenovarComCorpoVazio(Ct);
        var refreshNovo = renovacao.RefreshTokenDoCookie()!;

        await fabrica.RenovarComCookie(refreshOriginal, Ct);

        var aposDeteccao = await fabrica.RenovarComCookie(refreshNovo, Ct);

        aposDeteccao.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// O corpo é ignorado no modo cookie.
    /// </summary>
    /// <remarks>
    /// Aceitar o corpo como alternativa devolveria ao atacante o caminho que o cookie fechou:
    /// bastaria apresentar no corpo um token obtido de outro jeito.
    /// </remarks>
    [Fact]
    public async Task No_modo_cookie_o_refresh_token_do_corpo_e_ignorado()
    {
        var cliente = fabrica.CreateClient();
        var (_, refreshValido) = await cliente.RegistrarCapturandoCookie(Ct);

        var semCookie = fabrica.CreateClient();
        var resposta = await semCookie.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequestDTO(refreshValido), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Sessão inválida apaga o cookie.
    /// </summary>
    /// <remarks>
    /// Sem isso o navegador repetiria a mesma renovação fadada a falhar a cada carga da página.
    /// </remarks>
    [Fact]
    public async Task Renovacao_recusada_apaga_o_cookie()
    {
        var resposta = await fabrica.RenovarComCookie("token-que-nunca-existiu", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var setCookie = resposta.Headers.GetValues("Set-Cookie").Single(valor => valor.StartsWith("refresh_token=", StringComparison.Ordinal));

        setCookie.ShouldContain("expires=Thu, 01 Jan 1970", Case.Insensitive);
    }

    [Fact]
    public async Task Logout_invalida_o_refresh_token_e_apaga_o_cookie()
    {
        var cliente = fabrica.CreateClient();
        var (_, refresh) = await cliente.RegistrarCapturandoCookie(Ct);

        var logout = await cliente.SairComCorpoVazio(Ct);
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        logout.Headers.GetValues("Set-Cookie").ShouldContain(valor => valor.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

        var renovacao = await fabrica.RenovarComCookie(refresh, Ct);
        renovacao.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Erro_sai_no_formato_problem_details_com_traceId()
    {
        var resposta = await fabrica.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDTO("nao-e-email", ""), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var corpo = await resposta.Content.ReadAsStringAsync(Ct);
        corpo.ShouldContain("traceId");
        corpo.ShouldContain("codigo");
        corpo.ShouldContain("errors");
    }
}
