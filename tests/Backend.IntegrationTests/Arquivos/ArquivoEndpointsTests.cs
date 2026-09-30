using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Api.DTOs.Formandos;
using Backend.Business.Formaturas.Models;
using Backend.IntegrationTests.Infra;
using Shouldly;
using SkiaSharp;

namespace Backend.IntegrationTests.Arquivos;

/// <summary>
/// As fronteiras de acesso do módulo de arquivos contra a API real: download só para
/// o dono e para o administrador, e 404 — nunca 403 — para o arquivo de terceiro.
/// </summary>
/// <remarks>
/// O arquivo entra pelo endpoint da entidade que o justifica; aqui, a foto do próprio formando. Não
/// existe envio avulso, então a fronteira é exercitada sobre um arquivo com dono de domínio — que é
/// como todo arquivo do sistema nasce.
/// </remarks>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class ArquivoEndpointsTests(ApiFactory fabrica)
{
    private static readonly JsonSerializerOptions Json = JsonDaApi.Opcoes;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Um formando de turma nova, com a foto enviada; devolve o cliente dele e o id do arquivo.</summary>
    private async Task<(HttpClient Cliente, Guid ArquivoId)> FormandoComFoto()
    {
        var formaturaId = await fabrica.CriarFormatura(Ct);
        var formando = await fabrica.NovoMembro(formaturaId, PapelNaFormatura.Formando, Ct);

        var imagem = new ByteArrayContent(Png(64, 64));
        imagem.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var resposta = await formando.Cliente.PostAsync(
            "/api/v1/formandos/eu/foto",
            new MultipartFormDataContent { { imagem, "foto", "foto.png" } },
            Ct
        );

        resposta.EnsureSuccessStatusCode();

        var perfil = await resposta.Content.ReadFromJsonAsync<PerfilDoFormandoDTO>(Json, Ct);
        perfil!.FotoArquivoId.ShouldNotBeNull();

        return (formando.Cliente, perfil.FotoArquivoId.Value);
    }

    [Fact]
    public async Task O_dono_baixa_o_conteudo()
    {
        var (cliente, arquivoId) = await FormandoComFoto();

        var download = await cliente.GetAsync($"/api/v1/arquivos/{arquivoId}/conteudo", Ct);

        download.EnsureSuccessStatusCode();
        download.Content.Headers.ContentType?.MediaType.ShouldBe("image/jpeg");
        (await download.Content.ReadAsByteArrayAsync(Ct)).Length.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// A resposta é 404, e não 403: devolver 403 confirmaria que o identificador existe.
    /// </summary>
    [Fact]
    public async Task Arquivo_de_outro_usuario_responde_404()
    {
        var (_, arquivoId) = await FormandoComFoto();

        var intruso = fabrica.CreateClient();
        var tokens = await intruso.RegistrarUsuarioComum(Ct);
        intruso.ComToken(tokens.AccessToken);

        var download = await intruso.GetAsync($"/api/v1/arquivos/{arquivoId}/conteudo", Ct);

        download.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// D4 da Sprint 44: o administrador não tem passe livre nos arquivos. A foto do formando é dado pessoal que o
    /// painel não usa, e ele recebe o mesmo 404 de qualquer terceiro — nem a existência do arquivo se confirma.
    /// </summary>
    [Fact]
    public async Task O_administrador_nao_baixa_arquivo_de_outro_usuario()
    {
        var (_, arquivoId) = await FormandoComFoto();

        var admin = fabrica.CreateClient();
        var tokens = await admin.AutenticarComoAdministrador(Ct);

        var resposta = await admin.ComToken(tokens.AccessToken).GetAsync($"/api/v1/arquivos/{arquivoId}/conteudo", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sem_token_nao_se_chega_a_arquivo_nenhum()
    {
        var (_, arquivoId) = await FormandoComFoto();

        var resposta = await fabrica.CreateClient().GetAsync($"/api/v1/arquivos/{arquivoId}/conteudo", Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Não existe envio avulso nem listagem de arquivos: o arquivo sobe e é listado pelo endpoint da
    /// entidade que o justifica.
    /// </summary>
    /// <remarks>
    /// 404, e não 405: a rota <c>/arquivos</c> sem identificador não existe mais em método nenhum.
    /// </remarks>
    [Fact]
    public async Task Nao_existe_envio_avulso_nem_listagem_de_arquivos()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        cliente.ComToken(tokens.AccessToken);

        var envio = await cliente.PostAsync(
            "/api/v1/arquivos",
            new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "arquivo", "x.csv" } },
            Ct
        );
        var listagem = await cliente.GetAsync("/api/v1/arquivos", Ct);

        envio.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        listagem.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Metadados e remoção são do endpoint de domínio: o módulo de arquivos só serve o conteúdo, e a
    /// rota <c>/arquivos/{id}</c> não existe em método nenhum.
    /// </summary>
    [Fact]
    public async Task Nao_existe_metadado_nem_remocao_avulsa_de_arquivo()
    {
        var (cliente, arquivoId) = await FormandoComFoto();

        var metadados = await cliente.GetAsync($"/api/v1/arquivos/{arquivoId}", Ct);
        var remocao = await cliente.DeleteAsync($"/api/v1/arquivos/{arquivoId}", Ct);

        metadados.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        remocao.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static byte[] Png(int largura, int altura)
    {
        using var bitmap = new SKBitmap(largura, altura);
        bitmap.Erase(SKColors.Orange);
        using var imagem = SKImage.FromBitmap(bitmap);

        return imagem.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }
}
