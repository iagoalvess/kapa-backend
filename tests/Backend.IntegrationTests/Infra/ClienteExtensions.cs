using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Backend.Api.DTOs.Auth;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Backend.IntegrationTests.Infra;

/// <summary>
/// Atalhos de autenticação para os testes.
/// </summary>
public static class ClienteExtensions
{
    /// <summary>Registra uma conta nova com e-mail aleatório e devolve os tokens.</summary>
    /// <param name="cliente">Cliente HTTP da API de teste.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public static async Task<TokenResponseDTO> RegistrarUsuarioComum(this HttpClient cliente, CancellationToken ct)
    {
        var email = $"usuario-{Guid.CreateVersion7():N}@testes.local";

        var resposta = await cliente.PostAsJsonAsync(
            "/api/v1/auth/registrar",
            new RegistrarRequestDTO("Usuário de Teste", email, "Senha@Teste123"),
            ct
        );

        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(ct))!;
    }

    /// <summary>
    /// Registra uma conta com e-mail conhecido, para poder logar de novo depois.
    /// </summary>
    /// <remarks>
    /// <c>RegistrarUsuarioComum</c> sorteia o e-mail e não o devolve — serve para quem só precisa
    /// de um token, não para quem precisa exercitar um segundo login.
    /// </remarks>
    /// <param name="cliente">Cliente HTTP da API de teste.</param>
    /// <param name="email">E-mail da conta.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public static async Task<TokenResponseDTO> RegistrarComEmail(this HttpClient cliente, string email, CancellationToken ct)
    {
        var resposta = await cliente.PostAsJsonAsync("/api/v1/auth/registrar", new RegistrarRequestDTO("Usuário de Teste", email, SenhaPadrao), ct);

        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(ct))!;
    }

    /// <summary>Autentica uma conta registrada por <see cref="RegistrarComEmail"/>.</summary>
    /// <param name="cliente">Cliente HTTP da API de teste.</param>
    /// <param name="email">E-mail da conta.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public static async Task<TokenResponseDTO> AutenticarCom(this HttpClient cliente, string email, CancellationToken ct)
    {
        var resposta = await cliente.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDTO(email, SenhaPadrao), ct);

        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(ct))!;
    }

    /// <summary>Senha usada por todas as contas de teste.</summary>
    private const string SenhaPadrao = "Senha@Teste123";

    /// <summary>Autentica como o administrador criado pelo seed.</summary>
    /// <param name="cliente">Cliente HTTP da API de teste.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public static async Task<TokenResponseDTO> AutenticarComoAdministrador(this HttpClient cliente, CancellationToken ct)
    {
        var resposta = await cliente.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDTO(ApiFactory.AdminEmail, ApiFactory.AdminSenha), ct);

        resposta.EnsureSuccessStatusCode();

        return (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(ct))!;
    }

    /// <summary>Passa a enviar o token no cabeçalho <c>Authorization</c>.</summary>
    /// <param name="cliente">Cliente HTTP da API de teste.</param>
    /// <param name="accessToken">Token a usar.</param>
    public static HttpClient ComToken(this HttpClient cliente, string accessToken)
    {
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return cliente;
    }

    /// <summary>Registra uma conta nova e devolve os tokens junto do refresh token do cookie.</summary>
    /// <param name="cliente">Cliente HTTP da API de teste.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public static async Task<(TokenResponseDTO Tokens, string Refresh)> RegistrarCapturandoCookie(this HttpClient cliente, CancellationToken ct)
    {
        var email = $"usuario-{Guid.CreateVersion7():N}@testes.local";

        var resposta = await cliente.PostAsJsonAsync(
            "/api/v1/auth/registrar",
            new RegistrarRequestDTO("Usuário de Teste", email, "Senha@Teste123"),
            ct
        );

        resposta.EnsureSuccessStatusCode();

        return ((await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(ct))!, resposta.RefreshTokenDoCookie()!);
    }

    /// <summary>
    /// Extrai o valor do cookie de refresh de um <c>Set-Cookie</c>.
    /// </summary>
    /// <param name="resposta">Resposta a inspecionar.</param>
    /// <returns>O token, ou nulo se a resposta não gravou o cookie.</returns>
    public static string? RefreshTokenDoCookie(this HttpResponseMessage resposta)
    {
        if (!resposta.Headers.TryGetValues("Set-Cookie", out var cabecalhos))
            return null;

        var cookie = cabecalhos.FirstOrDefault(valor => valor.StartsWith($"{NomeDoCookie}=", StringComparison.Ordinal));

        var valorBruto = cookie?.Split(';')[0][(NomeDoCookie.Length + 1)..];

        return string.IsNullOrEmpty(valorBruto) ? null : valorBruto;
    }

    /// <summary>
    /// Chama a renovação apresentando um refresh token específico no cookie.
    /// </summary>
    /// <remarks>
    /// Replica o cenário de credencial copiada: apresentar um token que já foi rotacionado.
    /// <para>
    /// Usa um cliente com <c>HandleCookies = false</c> de propósito. O cliente comum da fábrica
    /// mantém o próprio pote de cookies e o atualiza a cada rotação — a requisição sairia com
    /// <b>dois</b> valores, e o servidor leria o novo, fazendo o teste de reúso passar por um
    /// motivo que não é o testado.
    /// </para>
    /// </remarks>
    /// <param name="fabrica">API de teste.</param>
    /// <param name="refreshToken">Token a apresentar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public static Task<HttpResponseMessage> RenovarComCookie(this ApiFactory fabrica, string refreshToken, CancellationToken ct)
    {
        var cliente = fabrica.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = false, BaseAddress = new Uri("https://localhost") }
        );

        var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        pedido.Headers.Add("Cookie", $"{NomeDoCookie}={refreshToken}");

        return cliente.SendAsync(pedido, ct);
    }

    /// <summary>
    /// Renova mandando um corpo <c>{}</c> literal — exatamente o que o front envia.
    /// </summary>
    /// <remarks>
    /// Não usa <c>PostAsJsonAsync(new RefreshRequestDTO(...))</c> de propósito. Serializar o DTO
    /// produz <c>{"refreshToken":""}</c>, que satisfaz a validação automática de campo
    /// obrigatório e <b>esconde</b> a regressão: com o campo declarado não-anulável, o corpo
    /// <c>{}</c> do cliente real leva 400 e o teste continuaria verde.
    /// </remarks>
    /// <param name="cliente">Cliente HTTP da API de teste.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public static Task<HttpResponseMessage> RenovarComCorpoVazio(this HttpClient cliente, CancellationToken ct) =>
        cliente.PostAsync("/api/v1/auth/refresh", new StringContent("{}", Encoding.UTF8, "application/json"), ct);

    /// <summary>Faz logout mandando um corpo <c>{}</c> literal, como o front.</summary>
    /// <param name="cliente">Cliente HTTP da API de teste.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public static Task<HttpResponseMessage> SairComCorpoVazio(this HttpClient cliente, CancellationToken ct) =>
        cliente.PostAsync("/api/v1/auth/logout", new StringContent("{}", Encoding.UTF8, "application/json"), ct);

    /// <summary>Nome do cookie, espelhando o padrão de <c>CookieDeSessao:Nome</c>.</summary>
    public const string NomeDoCookie = "refresh_token";
}
