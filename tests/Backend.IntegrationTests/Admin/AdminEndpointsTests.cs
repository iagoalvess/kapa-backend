using System.Net;
using System.Net.Http.Json;
using Backend.Api.DTOs.Admin;
using Backend.Business.Usuarios.Models;
using Backend.IntegrationTests.Infra;
using Shouldly;

namespace Backend.IntegrationTests.Admin;

/// <summary>
/// Verifica o painel administrativo e a fronteira que o protege.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class AdminEndpointsTests(ApiFactory fabrica)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sem_token_o_painel_responde_401()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/api/v1/admin/resumo", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Usuario_comum_nao_acessa_o_painel()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);

        var resposta = await cliente.ComToken(tokens.AccessToken).GetAsync("/api/v1/admin/resumo", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Administrador_recebe_o_resumo_com_numeros_coerentes()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(Ct);

        var resumo = await cliente.ComToken(tokens.AccessToken).GetFromJsonAsync<ResumoAdminDTO>("/api/v1/admin/resumo", Ct);

        resumo.ShouldNotBeNull();
        resumo.UsuariosTotal.ShouldBe(resumo.UsuariosAtivos + resumo.UsuariosInativos);
        resumo.Administradores.ShouldBeGreaterThanOrEqualTo(1);
        resumo.SessoesAtivas.ShouldBeGreaterThan(0);
        resumo.GeradoEm.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
    }

    [Fact]
    public async Task O_painel_expoe_os_perfis_que_o_sistema_reconhece()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.AutenticarComoAdministrador(Ct);

        var perfis = await cliente.ComToken(tokens.AccessToken).GetFromJsonAsync<string[]>("/api/v1/admin/perfis", Ct);

        perfis.ShouldBe([PerfisPadrao.Administrador, PerfisPadrao.Usuario], ignoreOrder: true);
    }
}
