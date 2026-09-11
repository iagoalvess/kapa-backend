using System.Net.Http.Json;
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
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Uma_acao_marcada_grava_o_evento_com_usuario_e_rota()
    {
        var clienteAlvo = fabrica.CreateClient();
        var alvo = await clienteAlvo.RegistrarUsuarioComum(Ct);
        var usuarioAlvo = await clienteAlvo.ComToken(alvo.AccessToken).GetFromJsonAsync<UsuarioDetalheDTO>("/api/v1/usuarios/eu", Ct);

        var clienteAdmin = fabrica.CreateClient();
        var admin = await clienteAdmin.AutenticarComoAdministrador(Ct);
        var administrador = await clienteAdmin.ComToken(admin.AccessToken).GetFromJsonAsync<UsuarioDetalheDTO>("/api/v1/usuarios/eu", Ct);

        var resposta = await clienteAdmin.PutAsJsonAsync(
            $"/api/v1/usuarios/{usuarioAlvo!.Id}/perfis",
            new AlterarPerfisRequestDTO(["Administrador", "Usuario"]),
            Ct
        );
        resposta.EnsureSuccessStatusCode();

        var evento = await EsperarEvento("usuario.perfis_alterados", Ct);

        evento.ShouldNotBeNull();
        evento.UsuarioId.ShouldBe(administrador!.Id);
        evento.Rota!.ShouldContain($"/api/v1/usuarios/{usuarioAlvo.Id}/perfis");
        evento.Dados.ShouldNotBeNull();
        evento.Dados!.ShouldContain(usuarioAlvo.Id.ToString());
    }

    /// <summary>
    /// Uma requisição recusada não vira evento — senão a métrica inflaria justamente nos
    /// períodos em que algo estava quebrado.
    /// </summary>
    [Fact]
    public async Task Requisicao_recusada_nao_gera_evento()
    {
        var cliente = fabrica.CreateClient();
        var admin = await cliente.AutenticarComoAdministrador(Ct);
        var eu = await cliente.ComToken(admin.AccessToken).GetFromJsonAsync<UsuarioDetalheDTO>("/api/v1/usuarios/eu", Ct);

        var antes = await ContarEventos("usuario.ativacao_alterada", Ct);

        var resposta = await cliente.PutAsJsonAsync($"/api/v1/usuarios/{eu!.Id}/ativacao", new AlterarAtivacaoRequestDTO(false), Ct);
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
