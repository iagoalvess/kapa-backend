using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

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
        var tokens = await cliente.AutenticarComoAdministrador(fabrica, Ct);

        var pagina = await cliente
            .ComToken(tokens.AccessToken)
            .GetFromJsonAsync<PaginaDTO<UsuarioResumoDTO>>("/api/v1/usuarios?pagina=1&tamanho=5", Json, Ct);

        pagina.ShouldNotBeNull();
        pagina.Pagina.ShouldBe(1);
        pagina.Tamanho.ShouldBe(5);
        pagina.Total.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task O_tamanho_de_pagina_e_limitado_pelo_servidor()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(fabrica, Ct);

        var pagina = await cliente
            .ComToken(tokens.AccessToken)
            .GetFromJsonAsync<PaginaDTO<UsuarioResumoDTO>>("/api/v1/usuarios?tamanho=100000", Json, Ct);

        pagina!.Tamanho.ShouldBe(100);
    }

    [Fact]
    public async Task Administrador_altera_o_cadastro_de_um_usuario()
    {
        var cliente = fabrica.CreateClient();
        var admin = await cliente.AutenticarComoAdministrador(fabrica, Ct);
        var alvo = await fabrica.CreateClient().RegistrarUsuarioComum(Ct);
        var alvoId = FormaturaDeTeste.IdDoUsuario(alvo.AccessToken);

        var alteracao = await cliente
            .ComToken(admin.AccessToken)
            .PutAsJsonAsync($"/api/v1/usuarios/{alvoId}", new AtualizarUsuarioRequestDTO("Nome Alterado"), Json, Ct);
        alteracao.EnsureSuccessStatusCode();

        var depois = (await alteracao.Content.ReadFromJsonAsync<UsuarioDetalheDTO>(Json, Ct))!;
        depois.Nome.ShouldBe("Nome Alterado");
        depois.Id.ShouldBe(alvoId);
        depois.Perfis.ShouldContain(PerfisPadrao.Usuario);
    }

    /// <summary>
    /// Não existe <c>/usuarios/eu</c>: o próprio cadastro do membro é lido e alterado por
    /// <c>/formandos/eu</c>.
    /// </summary>
    [Fact]
    public async Task Nao_existe_rota_do_proprio_usuario()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        var autenticado = cliente.ComToken(tokens.AccessToken);

        var leitura = await autenticado.GetAsync("/api/v1/usuarios/eu", Ct);
        var alteracao = await autenticado.PutAsJsonAsync("/api/v1/usuarios/eu", new AtualizarUsuarioRequestDTO("Nome"), Json, Ct);

        leitura.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        alteracao.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Nome_vazio_devolve_400_apontando_o_campo()
    {
        var cliente = fabrica.CreateClient();
        var admin = await cliente.AutenticarComoAdministrador(fabrica, Ct);
        var alvo = await fabrica.CreateClient().RegistrarUsuarioComum(Ct);

        var resposta = await cliente
            .ComToken(admin.AccessToken)
            .PutAsJsonAsync($"/api/v1/usuarios/{FormaturaDeTeste.IdDoUsuario(alvo.AccessToken)}", new AtualizarUsuarioRequestDTO(""), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadAsStringAsync(Ct)).ShouldContain("nome");
    }

    [Fact]
    public async Task Administrador_nao_consegue_remover_o_proprio_perfil()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(fabrica, Ct);
        var autenticado = cliente.ComToken(tokens.AccessToken);

        var eu = FormaturaDeTeste.IdDoUsuario(tokens.AccessToken);

        var resposta = await autenticado.PutAsJsonAsync(
            $"/api/v1/usuarios/{eu}/perfis",
            new AlterarPerfisRequestDTO([PerfisPadrao.Usuario]),
            Json,
            Ct
        );

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Administrador_nao_consegue_desativar_o_proprio_acesso()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(fabrica, Ct);
        var autenticado = cliente.ComToken(tokens.AccessToken);

        var eu = FormaturaDeTeste.IdDoUsuario(tokens.AccessToken);

        var resposta = await autenticado.PutAsJsonAsync($"/api/v1/usuarios/{eu}/ativacao", new AlterarAtivacaoRequestDTO(false), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Perfil_desconhecido_e_recusado()
    {
        var cliente = fabrica.CreateClient();
        var admin = await cliente.AutenticarComoAdministrador(fabrica, Ct);
        var alvo = await fabrica.CreateClient().RegistrarUsuarioComum(Ct);

        var autenticado = cliente.ComToken(admin.AccessToken);
        var eu = FormaturaDeTeste.IdDoUsuario(alvo.AccessToken);

        var resposta = await autenticado.PutAsJsonAsync($"/api/v1/usuarios/{eu}/perfis", new AlterarPerfisRequestDTO(["Superusuario"]), Json, Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Usuario_desativado_perde_o_acesso_na_renovacao()
    {
        var clienteAlvo = fabrica.CreateClient();
        var (alvo, refreshDoAlvo) = await clienteAlvo.RegistrarCapturandoCookie(Ct);
        var eu = FormaturaDeTeste.IdDoUsuario(alvo.AccessToken);

        var clienteAdmin = fabrica.CreateClient();
        var admin = await clienteAdmin.AutenticarComoAdministrador(fabrica, Ct);

        var desativacao = await clienteAdmin
            .ComToken(admin.AccessToken)
            .PutAsJsonAsync($"/api/v1/usuarios/{eu}/ativacao", new AlterarAtivacaoRequestDTO(false), Json, Ct);
        desativacao.EnsureSuccessStatusCode();

        var renovacao = await fabrica.RenovarComCookie(refreshDoAlvo, Ct);

        renovacao.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
