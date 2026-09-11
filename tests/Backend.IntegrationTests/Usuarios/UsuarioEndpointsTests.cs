using System.Net;
using System.Net.Http.Json;
using Backend.Api.DTOs.Auth;
using Backend.Api.DTOs.Comum;
using Backend.Api.DTOs.Usuarios;
using Backend.Business.Usuarios.Models;
using Backend.IntegrationTests.Infra;
using Shouldly;

namespace Backend.IntegrationTests.Usuarios;

/// <summary>
/// Verifica as fronteiras de acesso dos endpoints de usuário.
/// </summary>
/// <remarks>
/// Estes são os testes que mais valem a pena: uma política de autorização mal declarada não
/// quebra o build nem falha em teste unitário — só aparece como endpoint aberto em produção.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class UsuarioEndpointsTests(ApiFactory fabrica)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sem_token_a_listagem_responde_401()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/api/v1/usuarios", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Usuario_comum_nao_lista_usuarios()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);

        var resposta = await cliente.ComToken(tokens.AccessToken).GetAsync("/api/v1/usuarios", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Administrador_lista_usuarios_paginados()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(Ct);

        var pagina = await cliente
            .ComToken(tokens.AccessToken)
            .GetFromJsonAsync<PaginaDTO<UsuarioResumoDTO>>("/api/v1/usuarios?pagina=1&tamanho=5", Ct);

        pagina.ShouldNotBeNull();
        pagina.Pagina.ShouldBe(1);
        pagina.Tamanho.ShouldBe(5);
        pagina.Total.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task O_tamanho_de_pagina_e_limitado_pelo_servidor()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(Ct);

        var pagina = await cliente.ComToken(tokens.AccessToken).GetFromJsonAsync<PaginaDTO<UsuarioResumoDTO>>("/api/v1/usuarios?tamanho=100000", Ct);

        pagina!.Tamanho.ShouldBe(100);
    }

    [Fact]
    public async Task Usuario_autenticado_le_e_altera_o_proprio_cadastro()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        var autenticado = cliente.ComToken(tokens.AccessToken);

        var antes = await autenticado.GetFromJsonAsync<UsuarioDetalheDTO>("/api/v1/usuarios/eu", Ct);
        antes!.Perfis.ShouldContain(PerfisPadrao.Usuario);

        var alteracao = await autenticado.PutAsJsonAsync("/api/v1/usuarios/eu", new AtualizarUsuarioRequestDTO("Nome Alterado"), Ct);
        alteracao.EnsureSuccessStatusCode();

        var depois = (await alteracao.Content.ReadFromJsonAsync<UsuarioDetalheDTO>(Ct))!;
        depois.Nome.ShouldBe("Nome Alterado");
        depois.Id.ShouldBe(antes.Id);
    }

    [Fact]
    public async Task Nome_vazio_devolve_400_apontando_o_campo()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);

        var resposta = await cliente.ComToken(tokens.AccessToken).PutAsJsonAsync("/api/v1/usuarios/eu", new AtualizarUsuarioRequestDTO(""), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadAsStringAsync(Ct)).ShouldContain("nome");
    }

    [Fact]
    public async Task Administrador_nao_consegue_remover_o_proprio_perfil()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(Ct);
        var autenticado = cliente.ComToken(tokens.AccessToken);

        var eu = await autenticado.GetFromJsonAsync<UsuarioDetalheDTO>("/api/v1/usuarios/eu", Ct);

        var resposta = await autenticado.PutAsJsonAsync($"/api/v1/usuarios/{eu!.Id}/perfis", new AlterarPerfisRequestDTO([PerfisPadrao.Usuario]), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Administrador_nao_consegue_desativar_o_proprio_acesso()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(Ct);
        var autenticado = cliente.ComToken(tokens.AccessToken);

        var eu = await autenticado.GetFromJsonAsync<UsuarioDetalheDTO>("/api/v1/usuarios/eu", Ct);

        var resposta = await autenticado.PutAsJsonAsync($"/api/v1/usuarios/{eu!.Id}/ativacao", new AlterarAtivacaoRequestDTO(false), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Perfil_desconhecido_e_recusado()
    {
        var cliente = fabrica.CreateClient();
        var admin = await cliente.AutenticarComoAdministrador(Ct);
        var alvo = await fabrica.CreateClient().RegistrarUsuarioComum(Ct);

        var autenticado = cliente.ComToken(admin.AccessToken);
        var eu = await fabrica.CreateClient().ComToken(alvo.AccessToken).GetFromJsonAsync<UsuarioDetalheDTO>("/api/v1/usuarios/eu", Ct);

        var resposta = await autenticado.PutAsJsonAsync($"/api/v1/usuarios/{eu!.Id}/perfis", new AlterarPerfisRequestDTO(["Superusuario"]), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Usuario_desativado_perde_o_acesso_na_renovacao()
    {
        var clienteAlvo = fabrica.CreateClient();
        var (alvo, refreshDoAlvo) = await clienteAlvo.RegistrarCapturandoCookie(Ct);
        var eu = await clienteAlvo.ComToken(alvo.AccessToken).GetFromJsonAsync<UsuarioDetalheDTO>("/api/v1/usuarios/eu", Ct);

        var clienteAdmin = fabrica.CreateClient();
        var admin = await clienteAdmin.AutenticarComoAdministrador(Ct);

        var desativacao = await clienteAdmin
            .ComToken(admin.AccessToken)
            .PutAsJsonAsync($"/api/v1/usuarios/{eu!.Id}/ativacao", new AlterarAtivacaoRequestDTO(false), Ct);
        desativacao.EnsureSuccessStatusCode();

        var renovacao = await fabrica.RenovarComCookie(refreshDoAlvo, Ct);

        renovacao.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
