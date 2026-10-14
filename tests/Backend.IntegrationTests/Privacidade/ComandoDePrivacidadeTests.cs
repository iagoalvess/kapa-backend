using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Backend.Worker.Comandos;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Backend.IntegrationTests.Privacidade;

/// <summary>O comando do suporte gera o arquivo local sem publicar dados em endpoints.</summary>
[Collection(ColecaoDeApi.Nome)]
public sealed class ComandoDePrivacidadeTests(ApiFactory fabrica)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Exportacao_interna_recorta_o_titular_e_nao_sobrescreve_arquivo()
    {
        var turma = await fabrica.CriarFormatura(Ct);
        var titular = await fabrica.NovoMembro(turma, PapelNaFormatura.Formando, Ct);
        var terceiro = await fabrica.NovoMembro(turma, PapelNaFormatura.Formando, Ct);
        var destino = Path.Combine(Path.GetTempPath(), $"kapa-privacidade-{Guid.NewGuid():N}.zip");
        var argumentos = new[] { "privacidade", "exportar", titular.UsuarioId.ToString(), destino };
        using var saida = new StringWriter();

        try
        {
            (await ComandoDePrivacidade.Executar(argumentos, fabrica.Services, saida, Ct)).ShouldBe(0);
            var pacote = await File.ReadAllBytesAsync(destino, Ct);
            using (var memoria = new MemoryStream(pacote))
            using (var zip = new ZipArchive(memoria))
            using (var dados = JsonDocument.Parse(zip.GetEntry("dados.json")!.Open()))
            {
                dados.RootElement.GetProperty("conta").GetProperty("id").GetGuid().ShouldBe(titular.UsuarioId);
                dados.RootElement.GetProperty("turmas").EnumerateArray().ShouldHaveSingleItem().GetProperty("formaturaId").GetGuid().ShouldBe(turma);
                dados.RootElement.GetRawText().ShouldNotContain(terceiro.UsuarioId.ToString());
                dados.RootElement.GetRawText().ShouldNotContain("passwordHash");
            }

            (await ComandoDePrivacidade.Executar(argumentos, fabrica.Services, saida, Ct)).ShouldBe(2);
            (await File.ReadAllBytesAsync(destino, Ct)).ShouldBe(pacote);
        }
        finally
        {
            File.Delete(destino);
        }
    }

    [Fact]
    public async Task Titular_inexistente_nao_gera_arquivo()
    {
        var destino = Path.Combine(Path.GetTempPath(), $"kapa-privacidade-{Guid.NewGuid():N}.zip");
        using var saida = new StringWriter();
        (await ComandoDePrivacidade.Executar(["privacidade", "exportar", Guid.NewGuid().ToString(), destino], fabrica.Services, saida, Ct)).ShouldBe(
            1
        );
        File.Exists(destino).ShouldBeFalse();
    }

    [Fact]
    public async Task Limpeza_exige_confirmacao_e_registra_retirada_do_marketing()
    {
        var turma = await fabrica.CriarFormatura(Ct);
        var titular = await fabrica.NovoMembro(turma, PapelNaFormatura.Formando, Ct);
        using var saida = new StringWriter();

        (
            await ComandoDePrivacidade.Executar(["privacidade", "novidades", titular.UsuarioId.ToString(), "receber"], fabrica.Services, saida, Ct)
        ).ShouldBe(0);
        (
            await ComandoDePrivacidade.Executar(["privacidade", "excluir-cadastro", titular.UsuarioId.ToString(), ""], fabrica.Services, saida, Ct)
        ).ShouldBe(2);
        await using (var antes = fabrica.ContextoDe(null))
            (await antes.Users.SingleAsync(u => u.Id == titular.UsuarioId, Ct)).Ativo.ShouldBeTrue();

        (
            await ComandoDePrivacidade.Executar(
                ["privacidade", "excluir-cadastro", titular.UsuarioId.ToString(), "--confirmar"],
                fabrica.Services,
                saida,
                Ct
            )
        ).ShouldBe(0);
        await using var depois = fabrica.ContextoDe(null);
        var usuario = await depois.Users.SingleAsync(u => u.Id == titular.UsuarioId, Ct);
        usuario.Ativo.ShouldBeFalse();
        usuario.PasswordHash.ShouldBeNull();
        usuario.ReceberComunicacaoDoKapa.ShouldBeFalse();
        (
            await depois.ConsentimentosDeMarketing.AnyAsync(c => c.UsuarioId == titular.UsuarioId && !c.Aceito && c.Origem == "suporte", Ct)
        ).ShouldBeTrue();
    }

    [Theory]
    [InlineData("GET", "/api/v1/privacidade/meus-dados")]
    [InlineData("GET", "/api/v1/privacidade/operadores")]
    [InlineData("GET", "/api/v1/privacidade/solicitacoes")]
    [InlineData("POST", "/api/v1/privacidade/solicitacoes")]
    [InlineData("POST", "/api/v1/privacidade/consentimentos/0199386a-0000-7000-8000-000000000001/revogar")]
    [InlineData("PUT", "/api/v1/privacidade/comunicacao-do-kapa")]
    public async Task Portal_nao_expoe_mais_endpoints(string metodo, string rota)
    {
        using var pedido = new HttpRequestMessage(new HttpMethod(metodo), rota);
        using var resposta = await fabrica.CreateClient().SendAsync(pedido, Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
