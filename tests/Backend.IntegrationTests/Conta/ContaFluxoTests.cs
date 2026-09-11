using System.Net;
using System.Net.Http.Json;
using Backend.Api.DTOs.Auth;
using Backend.Api.DTOs.Conta;
using Backend.Business.Auth.Services;
using Backend.Business.Usuarios.Models;
using Backend.Data.Context;
using Backend.IntegrationTests.Infra;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Backend.IntegrationTests.Conta;

/// <summary>
/// Percorre o ciclo de vida da conta contra a API e o banco reais.
/// </summary>
/// <remarks>
/// Os tokens são gerados pelo <c>UserManager</c> dentro do teste, como o service faria. Ler o
/// token do e-mail enfileirado seria mais fiel, mas amarraria o teste ao HTML da mensagem — que
/// muda por projeto — em vez de amarrá-lo à regra.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class ContaFluxoTests(ApiFactory fabrica)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string SenhaNova = "NovaSenha@Teste456";

    private async Task<(string Email, TokenResponseDTO Tokens, string Refresh)> CriarConta()
    {
        var cliente = fabrica.CreateClient();
        var email = $"conta-{Guid.CreateVersion7():N}@testes.local";

        var resposta = await cliente.PostAsJsonAsync("/api/v1/auth/registrar", new RegistrarRequestDTO("Fulano", email, "Senha@Teste123"), Ct);
        resposta.EnsureSuccessStatusCode();

        return (email, (await resposta.Content.ReadFromJsonAsync<TokenResponseDTO>(Ct))!, resposta.RefreshTokenDoCookie()!);
    }

    private async Task<T> ComUserManager<T>(Func<UserManager<Usuario>, Task<T>> operacao)
    {
        using var escopo = fabrica.Services.CreateScope();

        return await operacao(escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>());
    }

    private Task<string> TokenDeRedefinicao(string email) =>
        ComUserManager(async manager =>
            CodificadorDeToken.Codificar(await manager.GeneratePasswordResetTokenAsync((await manager.FindByEmailAsync(email))!))
        );

    private Task<string> TokenDeConfirmacao(string email) =>
        ComUserManager(async manager =>
            CodificadorDeToken.Codificar(await manager.GenerateEmailConfirmationTokenAsync((await manager.FindByEmailAsync(email))!))
        );

    // ---------------------------------------------------------------- esqueci senha

    [Fact]
    public async Task Esqueci_senha_responde_204_para_conta_existente()
    {
        var (email, _, _) = await CriarConta();

        var resposta = await fabrica.CreateClient().PostAsJsonAsync("/api/v1/conta/esqueci-senha", new PedidoPorEmailRequestDTO(email), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// A resposta é idêntica para conta inexistente: distinguir os dois casos transformaria o
    /// endpoint num verificador de quais e-mails têm cadastro.
    /// </summary>
    [Fact]
    public async Task Esqueci_senha_responde_igual_para_conta_inexistente()
    {
        var resposta = await fabrica
            .CreateClient()
            .PostAsJsonAsync("/api/v1/conta/esqueci-senha", new PedidoPorEmailRequestDTO("ninguem@testes.local"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Esqueci_senha_enfileira_o_email_de_redefinicao()
    {
        var (email, _, _) = await CriarConta();

        var resposta = await fabrica.CreateClient().PostAsJsonAsync("/api/v1/conta/esqueci-senha", new PedidoPorEmailRequestDTO(email), Ct);
        resposta.EnsureSuccessStatusCode();

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();

        var enfileirado = await db.EmailsFila.AsNoTracking().Where(e => e.Para == email).OrderByDescending(e => e.CriadoEm).FirstOrDefaultAsync(Ct);

        enfileirado.ShouldNotBeNull();
        enfileirado.Assunto.ShouldContain("Redefinição de senha");
        enfileirado.CorpoHtml.ShouldContain("/redefinir-senha?email=");
        enfileirado.CorpoHtml.ShouldContain("token=");
    }

    [Fact]
    public async Task Esqueci_senha_com_email_invalido_responde_400()
    {
        var resposta = await fabrica.CreateClient().PostAsJsonAsync("/api/v1/conta/esqueci-senha", new PedidoPorEmailRequestDTO("nao-e-email"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- redefinir senha

    [Fact]
    public async Task Redefinir_troca_a_senha_e_a_antiga_para_de_funcionar()
    {
        var (email, _, _) = await CriarConta();
        var cliente = fabrica.CreateClient();

        var redefinicao = await cliente.PostAsJsonAsync(
            "/api/v1/conta/redefinir-senha",
            new RedefinirSenhaRequestDTO(email, await TokenDeRedefinicao(email), SenhaNova),
            Ct
        );
        redefinicao.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var comSenhaAntiga = await cliente.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDTO(email, "Senha@Teste123"), Ct);
        var comSenhaNova = await cliente.PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDTO(email, SenhaNova), Ct);

        comSenhaAntiga.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        comSenhaNova.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Se a redefinição aconteceu porque a conta foi comprometida, deixar os refresh tokens do
    /// atacante vivos anularia a troca de senha.
    /// </summary>
    [Fact]
    public async Task Redefinir_derruba_as_sessoes_abertas()
    {
        var (email, _, refresh) = await CriarConta();
        var cliente = fabrica.CreateClient();

        await cliente.PostAsJsonAsync(
            "/api/v1/conta/redefinir-senha",
            new RedefinirSenhaRequestDTO(email, await TokenDeRedefinicao(email), SenhaNova),
            Ct
        );

        var renovacao = await fabrica.RenovarComCookie(refresh, Ct);

        renovacao.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task O_token_de_redefinicao_so_pode_ser_usado_uma_vez()
    {
        var (email, _, _) = await CriarConta();
        var cliente = fabrica.CreateClient();
        var token = await TokenDeRedefinicao(email);

        var primeira = await cliente.PostAsJsonAsync("/api/v1/conta/redefinir-senha", new RedefinirSenhaRequestDTO(email, token, SenhaNova), Ct);
        var segunda = await cliente.PostAsJsonAsync(
            "/api/v1/conta/redefinir-senha",
            new RedefinirSenhaRequestDTO(email, token, "OutraSenha@789"),
            Ct
        );

        primeira.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        segunda.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Token_de_uma_conta_nao_redefine_a_senha_de_outra()
    {
        var (emailA, _, _) = await CriarConta();
        var (emailB, _, _) = await CriarConta();

        var resposta = await fabrica
            .CreateClient()
            .PostAsJsonAsync("/api/v1/conta/redefinir-senha", new RedefinirSenhaRequestDTO(emailB, await TokenDeRedefinicao(emailA), SenhaNova), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("token-inventado")]
    [InlineData("!!!nao-e-base64!!!")]
    public async Task Token_invalido_devolve_400_com_codigo_de_link_invalido(string token)
    {
        var (email, _, _) = await CriarConta();

        var resposta = await fabrica
            .CreateClient()
            .PostAsJsonAsync("/api/v1/conta/redefinir-senha", new RedefinirSenhaRequestDTO(email, token, SenhaNova), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadAsStringAsync(Ct)).ShouldContain("conta.link_invalido");
    }

    /// <summary>
    /// Token válido e senha fraca são problemas diferentes, com soluções opostas: pedir um link
    /// novo versus escolher outra senha.
    /// </summary>
    [Fact]
    public async Task Senha_fraca_na_redefinicao_devolve_o_motivo_e_nao_link_invalido()
    {
        var (email, _, _) = await CriarConta();

        var resposta = await fabrica
            .CreateClient()
            .PostAsJsonAsync("/api/v1/conta/redefinir-senha", new RedefinirSenhaRequestDTO(email, await TokenDeRedefinicao(email), "123"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var corpo = await resposta.Content.ReadAsStringAsync(Ct);
        corpo.ShouldContain("senha", Case.Insensitive);
        corpo.ShouldNotContain("conta.link_invalido");
    }

    // ---------------------------------------------------------------- confirmar e-mail

    [Fact]
    public async Task O_registro_enfileira_o_email_de_confirmacao()
    {
        var (email, _, _) = await CriarConta();

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();

        var enfileirado = await db.EmailsFila.AsNoTracking().FirstOrDefaultAsync(e => e.Para == email, Ct);

        enfileirado.ShouldNotBeNull();
        enfileirado.Assunto.ShouldContain("Confirme seu e-mail");
        enfileirado.CorpoHtml.ShouldContain("/confirmar-email?email=");
    }

    [Fact]
    public async Task Confirmar_marca_o_email_como_confirmado()
    {
        var (email, _, _) = await CriarConta();

        (await ComUserManager(async m => (await m.FindByEmailAsync(email))!.EmailConfirmed)).ShouldBeFalse();

        var resposta = await fabrica
            .CreateClient()
            .PostAsJsonAsync("/api/v1/conta/confirmar-email", new ConfirmarEmailRequestDTO(email, await TokenDeConfirmacao(email)), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ComUserManager(async m => (await m.FindByEmailAsync(email))!.EmailConfirmed)).ShouldBeTrue();
    }

    [Fact]
    public async Task Confirmar_duas_vezes_continua_respondendo_204()
    {
        var (email, _, _) = await CriarConta();
        var cliente = fabrica.CreateClient();
        var token = await TokenDeConfirmacao(email);

        await cliente.PostAsJsonAsync("/api/v1/conta/confirmar-email", new ConfirmarEmailRequestDTO(email, token), Ct);
        var segunda = await cliente.PostAsJsonAsync("/api/v1/conta/confirmar-email", new ConfirmarEmailRequestDTO(email, token), Ct);

        segunda.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Confirmar_com_token_invalido_devolve_400()
    {
        var (email, _, _) = await CriarConta();

        var resposta = await fabrica
            .CreateClient()
            .PostAsJsonAsync("/api/v1/conta/confirmar-email", new ConfirmarEmailRequestDTO(email, "token-inventado"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reenviar_confirmacao_responde_204_mesmo_para_conta_inexistente()
    {
        var resposta = await fabrica
            .CreateClient()
            .PostAsJsonAsync("/api/v1/conta/reenviar-confirmacao", new PedidoPorEmailRequestDTO("ninguem@testes.local"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // ---------------------------------------------------------------- alterar senha

    [Fact]
    public async Task Alterar_senha_exige_autenticacao()
    {
        var resposta = await fabrica
            .CreateClient()
            .PostAsJsonAsync("/api/v1/conta/alterar-senha", new AlterarSenhaRequestDTO("Senha@Teste123", SenhaNova), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Um access token esquecido numa máquina aberta não deve bastar para trocar a senha e tomar
    /// a conta — por isso a senha atual é exigida mesmo com o usuário autenticado.
    /// </summary>
    [Fact]
    public async Task Alterar_senha_com_a_senha_atual_errada_e_recusado()
    {
        var (_, tokens, _) = await CriarConta();

        var resposta = await fabrica
            .CreateClient()
            .ComToken(tokens.AccessToken)
            .PostAsJsonAsync("/api/v1/conta/alterar-senha", new AlterarSenhaRequestDTO("SenhaErrada@123", SenhaNova), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Alterar_senha_troca_a_senha_e_derruba_as_sessoes()
    {
        var (email, tokens, refresh) = await CriarConta();
        var cliente = fabrica.CreateClient();

        var troca = await cliente
            .ComToken(tokens.AccessToken)
            .PostAsJsonAsync("/api/v1/conta/alterar-senha", new AlterarSenhaRequestDTO("Senha@Teste123", SenhaNova), Ct);
        troca.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var renovacao = await fabrica.RenovarComCookie(refresh, Ct);
        renovacao.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var login = await fabrica.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequestDTO(email, SenhaNova), Ct);
        login.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_nova_senha_precisa_ser_diferente_da_atual()
    {
        var (_, tokens, _) = await CriarConta();

        var resposta = await fabrica
            .CreateClient()
            .ComToken(tokens.AccessToken)
            .PostAsJsonAsync("/api/v1/conta/alterar-senha", new AlterarSenhaRequestDTO("Senha@Teste123", "Senha@Teste123"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Alterar_senha_enfileira_o_aviso_para_o_dono_da_conta()
    {
        var (email, tokens, _) = await CriarConta();

        await fabrica
            .CreateClient()
            .ComToken(tokens.AccessToken)
            .PostAsJsonAsync("/api/v1/conta/alterar-senha", new AlterarSenhaRequestDTO("Senha@Teste123", SenhaNova), Ct);

        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();

        var aviso = await db.EmailsFila.AsNoTracking().Where(e => e.Para == email).OrderByDescending(e => e.CriadoEm).FirstOrDefaultAsync(Ct);

        aviso.ShouldNotBeNull();
        aviso.Assunto.ShouldContain("Sua senha foi alterada");
    }
}
