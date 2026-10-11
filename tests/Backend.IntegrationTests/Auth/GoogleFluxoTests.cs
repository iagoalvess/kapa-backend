using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Auth;
using Backend.Business.Auth.Models;
using Backend.Business.Auth.Services;
using Backend.Business.Auth.Settings;
using Backend.Business.Usuarios.Models;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace Backend.IntegrationTests.Auth;

/// <summary>SSO contra API e PostgreSQL reais; apenas as chaves públicas do Google são locais ao teste.</summary>
[Collection(ColecaoDeApi.Nome)]
public sealed class GoogleFluxoTests(ApiFactory fabrica)
{
    private const string ClientId = "teste.apps.googleusercontent.com";
    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly RsaSecurityKey Chave = new(RSA.Create(2048)) { KeyId = "google-teste" };

    private HttpClient Cliente()
    {
        var config = new OpenIdConnectConfiguration();
        config.SigningKeys.Add(Chave);
        var host = fabrica.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.Configure<GoogleSettings>(opcoes => opcoes.ClientId = ClientId);
                services.RemoveAll<OrigensPermitidas>();
                services.AddSingleton(new OrigensPermitidas(["http://localhost:5173"]));
                services.RemoveAll<IConfigurationManager<OpenIdConnectConfiguration>>();
                services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(config)
                );
            })
        );
        var cliente = host.CreateClient();
        cliente.DefaultRequestHeaders.Add("Origin", "http://localhost:5173");
        return cliente;
    }

    private static string Credencial(string nonce, string email, string sub, string variante = "", bool workspace = false)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = sub,
            ["email"] = email,
            ["name"] = "Pessoa do Google",
            ["nonce"] = variante == "nonce" ? "outro" : nonce,
            ["email_verified"] = variante != "email",
        };
        if (workspace)
            claims["hd"] = "empresa.test";
        return new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = variante == "iss" ? "https://invasor.test" : "https://accounts.google.com",
                Audience = variante == "aud" ? "outro-app" : ClientId,
                IssuedAt = DateTime.UtcNow.AddMinutes(-5),
                NotBefore = DateTime.UtcNow.AddMinutes(-5),
                Expires = variante == "exp" ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(10),
                Claims = claims,
                SigningCredentials = new SigningCredentials(
                    variante == "assinatura" ? new RsaSecurityKey(RSA.Create(2048)) { KeyId = "google-teste" } : Chave,
                    SecurityAlgorithms.RsaSha256
                ),
            }
        );
    }

    private static async Task<InicioGoogle> Iniciar(HttpClient cliente)
    {
        var resposta = await cliente.PostAsJsonAsync("/api/v1/auth/google/iniciar", new { }, Ct);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<InicioGoogle>(Json, Ct))!;
    }

    private static async Task<HttpResponseMessage> Entrar(HttpClient cliente, string email, string sub, string variante = "", bool workspace = false)
    {
        var inicio = await Iniciar(cliente);
        return await cliente.PostAsJsonAsync(
            "/api/v1/auth/google",
            new { tentativa = inicio.Tentativa, credencial = Credencial(inicio.Nonce, email, sub, variante, workspace) },
            Ct
        );
    }

    private static async Task<ContextoGoogle> Contexto(HttpResponseMessage resposta)
    {
        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        return corpo.GetProperty("dados").Deserialize<ContextoGoogle>(Json)!;
    }

    [Theory]
    [InlineData("aud", HttpStatusCode.Unauthorized)]
    [InlineData("iss", HttpStatusCode.Unauthorized)]
    [InlineData("exp", HttpStatusCode.Unauthorized)]
    [InlineData("nonce", HttpStatusCode.Unauthorized)]
    [InlineData("assinatura", HttpStatusCode.Unauthorized)]
    [InlineData("email", HttpStatusCode.Forbidden)]
    public async Task Recusa_credencial_sem_comprovacao_valida(string variante, HttpStatusCode esperado)
    {
        using var cliente = Cliente();
        (await Entrar(cliente, "pessoa@gmail.com", Guid.NewGuid().ToString(), variante)).StatusCode.ShouldBe(esperado);
    }

    [Fact]
    public async Task Sem_configuracao_ou_origem_valida_nao_inicia()
    {
        using var desligado = fabrica.CreateClient();
        (await desligado.PostAsJsonAsync("/api/v1/auth/google/iniciar", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var cliente = Cliente();
        cliente.DefaultRequestHeaders.Remove("Origin");
        cliente.DefaultRequestHeaders.Add("Origin", "https://invasor.test");
        (await cliente.PostAsJsonAsync("/api/v1/auth/google/iniciar", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cadastro_exige_aceites_cria_sem_senha_e_contexto_nao_repete()
    {
        using var cliente = Cliente();
        var email = $"novo-{Guid.NewGuid():N}@gmail.com";
        var sub = Guid.NewGuid().ToString();
        var contexto = await Contexto(await Entrar(cliente, email, sub));
        (
            await cliente.PostAsJsonAsync(
                "/api/v1/auth/google/cadastro",
                new
                {
                    contexto = contexto.Contexto,
                    nome = "Pessoa",
                    aceites = Array.Empty<object>(),
                },
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var cadastro = await cliente.CorpoDeCadastro("Pessoa", email, "Senha@Teste123", Ct);
        var corpo = new
        {
            contexto = contexto.Contexto,
            nome = cadastro.Nome,
            aceites = cadastro.Aceites,
            receber_comunicacao_do_kapa = false,
        };
        var resposta = await cliente.PostAsJsonAsync("/api/v1/auth/google/cadastro", corpo, Json, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);
        var sessao = await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(Json, Ct);
        sessao!.RefreshToken.ShouldBeNull();
        resposta.Headers.GetValues("Set-Cookie").ShouldContain(c => c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        await using var db = fabrica.ContextoDe(null);
        var usuario = await db.Users.SingleAsync(x => x.Email == email, Ct);
        usuario.PasswordHash.ShouldBeNull();
        usuario.EmailConfirmed.ShouldBeTrue();
        usuario.ReceberComunicacaoDoKapa.ShouldBeFalse();
        (await cliente.PostAsJsonAsync("/api/v1/auth/google/cadastro", corpo, Json, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Entrar(cliente, "mudou@gmail.com", sub)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var repetido = await cliente.PostAsJsonAsync("/api/v1/auth/registrar", cadastro, Json, Ct);
        repetido.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await db.Users.AsNoTracking().SingleAsync(x => x.Email == email, Ct)).PasswordHash.ShouldBeNull();
    }

    [Fact]
    public async Task Tentativa_nao_pode_ser_repetida_roubada_ou_usada_depois_do_prazo()
    {
        using var cliente = Cliente();
        var inicio = await Iniciar(cliente);
        var dados = new
        {
            tentativa = inicio.Tentativa,
            credencial = Credencial(inicio.Nonce, $"teste-{Guid.NewGuid():N}@gmail.com", Guid.NewGuid().ToString()),
        };
        using var outro = Cliente();
        await Iniciar(outro);
        (await outro.PostAsJsonAsync("/api/v1/auth/google", dados, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await cliente.PostAsJsonAsync("/api/v1/auth/google", dados, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await cliente.PostAsJsonAsync("/api/v1/auth/google", dados, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var vencida = await Iniciar(cliente);
        await using var db = fabrica.ContextoDe(null);
        var tentativa = await db.Set<TentativaGoogle>().SingleAsync(x => x.TokenHash == GoogleService.Hash(vencida.Tentativa), Ct);
        tentativa.ExpiraEm = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync(Ct);
        (
            await cliente.PostAsJsonAsync(
                "/api/v1/auth/google",
                new { tentativa = vencida.Tentativa, credencial = Credencial(vencida.Nonce, "pessoa@gmail.com", Guid.NewGuid().ToString()) },
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Administrador_nao_vincula_google_e_senha_continua_exigindo_codigo()
    {
        using var cliente = Cliente();
        (await Entrar(cliente, ApiFactory.AdminEmail, Guid.NewGuid().ToString(), workspace: true)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var escopo = fabrica.Services.CreateScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        (await usuarios.GetLoginsAsync((await usuarios.FindByEmailAsync(ApiFactory.AdminEmail))!)).ShouldBeEmpty();
        (
            await cliente.PostAsJsonAsync("/api/v1/auth/login", new { email = ApiFactory.AdminEmail, senha = ApiFactory.AdminSenha }, Ct)
        ).StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Vinculo_de_email_terceiro_exige_link_e_nao_transfere_identidade()
    {
        using var cliente = Cliente();
        var email = $"terceiro-{Guid.NewGuid():N}@empresa.test";
        var cadastro = await cliente.CorpoDeCadastro("Pessoa", email, "Senha@Teste123", Ct);
        (await cliente.PostAsJsonAsync("/api/v1/auth/registrar", cadastro, Json, Ct)).EnsureSuccessStatusCode();
        using var escopo = fabrica.Services.CreateScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var usuario = (await usuarios.FindByEmailAsync(email))!;
        await usuarios.ConfirmEmailAsync(usuario, await usuarios.GenerateEmailConfirmationTokenAsync(usuario));
        var sub = Guid.NewGuid().ToString();
        var contexto = await Contexto(await Entrar(cliente, email, sub));
        var solicitacao = await cliente.PostAsJsonAsync("/api/v1/auth/google/vinculo/solicitar", new { contexto = contexto.Contexto }, Ct);
        solicitacao.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var mensagem = (await fabrica.UltimoEmailDaFila(Ct))!;
        var comprovante = Uri.UnescapeDataString(Regex.Match(mensagem, "comprovante_google=([^&\\\"]+)").Groups[1].Value);
        comprovante.ShouldNotBeNullOrEmpty();
        var dados = new { contexto = contexto.Contexto, comprovante };
        (await cliente.PostAsJsonAsync("/api/v1/auth/google/vinculo", dados, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cliente.PostAsJsonAsync("/api/v1/auth/google/vinculo", dados, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Entrar(cliente, "outro@empresa.test", sub)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await usuarios.FindByLoginAsync("Google", sub))!.Id.ShouldBe(usuario.Id);
    }

    [Fact]
    public async Task Cadastros_concorrentes_do_mesmo_email_criam_apenas_uma_conta()
    {
        using var primeiro = Cliente();
        using var segundo = Cliente();
        var email = $"concorrente-{Guid.NewGuid():N}@gmail.com";
        var a = await Contexto(await Entrar(primeiro, email, Guid.NewGuid().ToString()));
        var b = await Contexto(await Entrar(segundo, email, Guid.NewGuid().ToString()));
        var cadastro = await primeiro.CorpoDeCadastro("Pessoa", email, "Senha@Teste123", Ct);
        var resultados = await Task.WhenAll(
            primeiro.PostAsJsonAsync(
                "/api/v1/auth/google/cadastro",
                new
                {
                    contexto = a.Contexto,
                    nome = "Pessoa",
                    aceites = cadastro.Aceites,
                },
                Json,
                Ct
            ),
            segundo.PostAsJsonAsync(
                "/api/v1/auth/google/cadastro",
                new
                {
                    contexto = b.Contexto,
                    nome = "Pessoa",
                    aceites = cadastro.Aceites,
                },
                Json,
                Ct
            )
        );
        resultados.Select(x => x.StatusCode).ShouldContain(HttpStatusCode.Created);
        resultados.Select(x => x.StatusCode).ShouldContain(HttpStatusCode.Conflict);
        await using var db = fabrica.ContextoDe(null);
        (await db.Users.CountAsync(x => x.Email == email, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Conta_confirmada_gmail_vincula_sem_alterar_senha_e_inativa_nao_entra()
    {
        using var cliente = Cliente();
        var email = $"confirmada-{Guid.NewGuid():N}@gmail.com";
        await cliente.RegistrarComEmail(email, Ct);
        using var escopo = fabrica.Services.CreateScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var usuario = (await usuarios.FindByEmailAsync(email))!;
        await usuarios.ConfirmEmailAsync(usuario, await usuarios.GenerateEmailConfirmationTokenAsync(usuario));
        var senha = usuario.PasswordHash;
        var sub = Guid.NewGuid().ToString();
        (await Entrar(cliente, email, sub)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var db = fabrica.ContextoDe(null);
        var atual = await db.Users.SingleAsync(x => x.Id == usuario.Id, Ct);
        atual.PasswordHash.ShouldBe(senha);
        atual.Ativo = false;
        await db.SaveChangesAsync(Ct);
        (await Entrar(cliente, email, sub)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Entrar(cliente, email, Guid.NewGuid().ToString())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Conta_antiga_exige_redefinicao_encerra_sessoes_e_link_abre_com_cookie_e_convite()
    {
        using var cliente = Cliente();
        var email = $"antiga-{Guid.NewGuid():N}@gmail.com";
        var cadastro = await cliente.CorpoDeCadastro("Pessoa", email, "Senha@Teste123", Ct);
        var registro = await cliente.PostAsJsonAsync("/api/v1/auth/registrar", cadastro, Json, Ct);
        registro.EnsureSuccessStatusCode();
        var refresh = registro.RefreshTokenDoCookie()!;
        var inicioResposta = await cliente.PostAsJsonAsync("/api/v1/auth/google/iniciar", new { convite = "convite-publico" }, Ct);
        var inicio = (await inicioResposta.Content.ReadFromJsonAsync<InicioGoogle>(Json, Ct))!;
        var sub = Guid.NewGuid().ToString();
        var contexto = await Contexto(
            await cliente.PostAsJsonAsync(
                "/api/v1/auth/google",
                new { tentativa = inicio.Tentativa, credencial = Credencial(inicio.Nonce, email, sub) },
                Ct
            )
        );
        contexto.ExigeRecuperacao.ShouldBeTrue();
        (await cliente.PostAsJsonAsync("/api/v1/auth/google/vinculo/solicitar", new { contexto = contexto.Contexto }, Ct)).EnsureSuccessStatusCode();
        var mensagem = (await fabrica.UltimoEmailDaFila(Ct))!;
        mensagem.ShouldContain("convite=convite-publico");
        var comprovante = Uri.UnescapeDataString(Regex.Match(mensagem, "comprovante_google=([^&\\\"]+)").Groups[1].Value);
        var link = new { contexto = (string?)null, comprovante };
        (await cliente.PostAsJsonAsync("/api/v1/auth/google/vinculo", link, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var escopo = fabrica.Services.CreateScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var usuario = (await usuarios.FindByEmailAsync(email))!;
        var token = CodificadorDeToken.Codificar(await usuarios.GeneratePasswordResetTokenAsync(usuario));
        (
            await cliente.PostAsJsonAsync(
                "/api/v1/conta/redefinir-senha",
                new
                {
                    email,
                    token,
                    nova_senha = "NovaSenha@Teste123",
                },
                Ct
            )
        ).EnsureSuccessStatusCode();
        (await fabrica.RenovarComCookie(refresh, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await cliente.PostAsJsonAsync("/api/v1/auth/google/vinculo", link, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cliente.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = "Senha@Teste123" }, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized
        );
        (await cliente.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = "NovaSenha@Teste123" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Entrar(cliente, email, sub)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Primeira_senha_de_google_usa_recuperacao_sem_mutacao_na_solicitacao_e_link_so_uma_vez()
    {
        using var cliente = Cliente();
        var email = $"sem-senha-{Guid.NewGuid():N}@gmail.com";
        var sub = Guid.NewGuid().ToString();
        var contexto = await Contexto(await Entrar(cliente, email, sub));
        var cadastro = await cliente.CorpoDeCadastro("Pessoa", email, "Senha@Teste123", Ct);
        var registro = await cliente.PostAsJsonAsync(
            "/api/v1/auth/google/cadastro",
            new
            {
                contexto = contexto.Contexto,
                nome = "Pessoa",
                aceites = cadastro.Aceites,
            },
            Json,
            Ct
        );
        registro.EnsureSuccessStatusCode();
        var refresh = registro.RefreshTokenDoCookie()!;
        var sessao = (await registro.Content.ReadFromJsonAsync<TokenResponseDTO>(Json, Ct))!;
        cliente.ComToken(sessao.AccessToken);
        var acesso = await cliente.GetFromJsonAsync<JsonElement>("/api/v1/conta/acesso", Ct);
        acesso.GetProperty("tem_senha").GetBoolean().ShouldBeFalse();
        (await cliente.PostAsJsonAsync("/api/v1/conta/esqueci-senha", new { email }, Ct)).EnsureSuccessStatusCode();
        using var escopo = fabrica.Services.CreateScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var usuario = (await usuarios.FindByEmailAsync(email))!;
        usuario.PasswordHash.ShouldBeNull();
        var token = CodificadorDeToken.Codificar(await usuarios.GeneratePasswordResetTokenAsync(usuario));
        var redefinicao = new
        {
            email,
            token,
            nova_senha = "PrimeiraSenha@Teste123",
        };
        (await cliente.PostAsJsonAsync("/api/v1/conta/redefinir-senha", redefinicao, Ct)).EnsureSuccessStatusCode();
        (await cliente.PostAsJsonAsync("/api/v1/conta/redefinir-senha", redefinicao, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await fabrica.RenovarComCookie(refresh, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await cliente.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = redefinicao.nova_senha }, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.OK
        );
        (await Entrar(cliente, email, sub)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
