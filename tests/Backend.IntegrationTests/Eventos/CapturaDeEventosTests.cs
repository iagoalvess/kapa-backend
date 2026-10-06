using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Usuarios;
using Backend.Business.Eventos.Models;
using Backend.Data.Context;
using Backend.IntegrationTests.Infra;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Backend.IntegrationTests.Eventos;

/// <summary>
/// Percorre o caminho completo do evento: atributo no controller, fila em memória, serviço de
/// descarga e tabela.
/// </summary>
/// <remarks>
/// Testar as peças isoladamente não provaria o que interessa. O que quebra na prática é a
/// ligação — o filtro que não foi registrado, a fila que não tem consumidor, o escopo que não
/// resolve o repositório.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class CapturaDeEventosTests(ApiFactory fabrica)
{
    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Uma_acao_marcada_grava_o_evento_com_usuario_e_dados()
    {
        var clienteAlvo = fabrica.CreateClient();
        var alvo = await clienteAlvo.RegistrarUsuarioComum(Ct);
        var usuarioAlvoId = FormaturaDeTeste.IdDoUsuario(alvo.AccessToken);

        var clienteAdmin = fabrica.CreateClient();
        var admin = await clienteAdmin.AutenticarComoAdministrador(fabrica, Ct);
        var administradorId = FormaturaDeTeste.IdDoUsuario(admin.AccessToken);

        var resposta = await clienteAdmin
            .ComToken(admin.AccessToken)
            .PutAsJsonAsync($"/api/v1/usuarios/{usuarioAlvoId}/perfis", new AlterarPerfisRequestDTO(["Administrador", "Usuario"]), Json, Ct);
        resposta.EnsureSuccessStatusCode();

        var evento = await EsperarEvento("usuario.perfis_alterados", Ct);

        evento.ShouldNotBeNull();
        evento.UsuarioId.ShouldBe(administradorId);
        evento.Dados.ShouldNotBeNull();
        evento.Dados!.ShouldContain(usuarioAlvoId.ToString());
    }

    /// <summary>
    /// Uma requisição recusada não vira evento — senão a métrica inflaria justamente nos
    /// períodos em que algo estava quebrado.
    /// </summary>
    [Fact]
    public async Task Requisicao_recusada_nao_gera_evento()
    {
        var cliente = fabrica.CreateClient();
        var admin = await cliente.AutenticarComoAdministrador(fabrica, Ct);
        var eu = FormaturaDeTeste.IdDoUsuario(admin.AccessToken);
        cliente.ComToken(admin.AccessToken);

        var antes = await ContarEventos("usuario.ativacao_alterada", Ct);

        var resposta = await cliente.PutAsJsonAsync($"/api/v1/usuarios/{eu}/ativacao", new AlterarAtivacaoRequestDTO(false), Json, Ct);
        resposta.IsSuccessStatusCode.ShouldBeFalse();

        await Task.Delay(TimeSpan.FromSeconds(1), Ct);

        (await ContarEventos("usuario.ativacao_alterada", Ct)).ShouldBe(antes);
    }

    private async Task<Evento?> EsperarEvento(string nome, CancellationToken ct)
    {
        for (var tentativa = 0; tentativa < 20; tentativa++)
        {
            using var escopo = fabrica.Services.CreateScope();
            var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();

            var evento = await db.Eventos.AsNoTracking().Where(e => e.Nome == nome).OrderByDescending(e => e.OcorridoEm).FirstOrDefaultAsync(ct);

            if (evento is not null)
                return evento;

            await Task.Delay(TimeSpan.FromMilliseconds(150), ct);
        }

        return null;
    }

    private async Task<int> ContarEventos(string nome, CancellationToken ct)
    {
        using var escopo = fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Eventos.AsNoTracking().CountAsync(e => e.Nome == nome, ct);
    }
}
