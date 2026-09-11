using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Backend.Api.DTOs.Arquivos;
using Backend.Api.DTOs.Comum;
using Backend.IntegrationTests.Infra;
using Shouldly;

namespace Backend.IntegrationTests.Arquivos;

/// <summary>
/// Percorre o ciclo completo de um arquivo contra a API real: envio por multipart, leitura dos
/// metadados, download dos bytes e remoção.
/// </summary>
/// <param name="fabrica">API de teste compartilhada.</param>
[Collection(ColecaoDeApi.Nome)]
public sealed class ArquivoEndpointsTests(ApiFactory fabrica)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Conteudo = "linha 1;linha 2;linha 3";

    private static MultipartFormDataContent Multipart(string nome, string categoria, string conteudo = Conteudo)
    {
        var arquivo = new ByteArrayContent(Encoding.UTF8.GetBytes(conteudo));
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

        return new MultipartFormDataContent { { arquivo, "arquivo", nome }, { new StringContent(categoria), "categoria" } };
    }

    private async Task<(HttpClient Cliente, ArquivoResumoDTO Arquivo)> EnviarComoUsuarioNovo(string nome = "dados.csv", string categoria = "anexos")
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        cliente.ComToken(tokens.AccessToken);

        var resposta = await cliente.PostAsync("/api/v1/arquivos", Multipart(nome, categoria), Ct);
        resposta.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (cliente, (await resposta.Content.ReadFromJsonAsync<ArquivoResumoDTO>(Ct))!);
    }

    [Fact]
    public async Task Envia_e_devolve_os_metadados_com_Location()
    {
        var (_, arquivo) = await EnviarComoUsuarioNovo();

        arquivo.Nome.ShouldBe("dados.csv");
        arquivo.Categoria.ShouldBe("anexos");
        arquivo.Tamanho.ShouldBe(Encoding.UTF8.GetByteCount(Conteudo));
        arquivo.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task O_download_devolve_os_mesmos_bytes_e_o_nome_original()
    {
        var (cliente, arquivo) = await EnviarComoUsuarioNovo();

        var resposta = await cliente.GetAsync($"/api/v1/arquivos/{arquivo.Id}/conteudo", Ct);
        resposta.EnsureSuccessStatusCode();

        (await resposta.Content.ReadAsStringAsync(Ct)).ShouldBe(Conteudo);
        resposta.Content.Headers.ContentDisposition?.FileName?.Trim('"').ShouldBe("dados.csv");
    }

    [Fact]
    public async Task Sem_token_o_envio_responde_401()
    {
        var resposta = await fabrica.CreateClient().PostAsync("/api/v1/arquivos", Multipart("x.csv", "anexos"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Extensao_fora_da_lista_de_permissao_responde_400()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);

        var resposta = await cliente.ComToken(tokens.AccessToken).PostAsync("/api/v1/arquivos", Multipart("payload.exe", "anexos"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadAsStringAsync(Ct)).ShouldContain("Extensão", Case.Insensitive);
    }

    [Fact]
    public async Task Categoria_com_espaco_responde_400()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);

        var resposta = await cliente.ComToken(tokens.AccessToken).PostAsync("/api/v1/arquivos", Multipart("x.csv", "Anexos do Pedido"), Ct);

        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A cota por usuário vale contra o banco real.
    /// </summary>
    /// <remarks>
    /// Aqui, e não só no unitário, porque a contagem sai de um <c>count/sum</c> agregado que o EF
    /// precisa traduzir para SQL — e <c>sum</c> sobre conjunto vazio devolve <c>NULL</c> no
    /// Postgres. Substituto de repositório não pega nenhum dos dois.
    /// </remarks>
    [Fact]
    public async Task Passar_do_limite_de_arquivos_do_usuario_responde_409()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        cliente.ComToken(tokens.AccessToken);

        for (var i = 0; i < ApiFactory.LimiteDeArquivos; i++)
        {
            var aceito = await cliente.PostAsync("/api/v1/arquivos", Multipart($"dados-{i}.csv", "anexos"), Ct);
            aceito.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var recusado = await cliente.PostAsync("/api/v1/arquivos", Multipart("excedente.csv", "anexos"), Ct);

        recusado.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await recusado.Content.ReadAsStringAsync(Ct)).ShouldContain("arquivo.limite_de_quantidade");
    }

    /// <summary>
    /// A resposta é 404, e não 403: devolver 403 confirmaria que o identificador existe.
    /// </summary>
    [Fact]
    public async Task Arquivo_de_outro_usuario_responde_404()
    {
        var (_, arquivo) = await EnviarComoUsuarioNovo();

        var intruso = fabrica.CreateClient();
        var tokens = await intruso.RegistrarUsuarioComum(Ct);

        var metadados = await intruso.ComToken(tokens.AccessToken).GetAsync($"/api/v1/arquivos/{arquivo.Id}", Ct);
        var download = await intruso.GetAsync($"/api/v1/arquivos/{arquivo.Id}/conteudo", Ct);

        metadados.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        download.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task O_administrador_baixa_arquivo_de_qualquer_usuario()
    {
        var (_, arquivo) = await EnviarComoUsuarioNovo();

        var admin = fabrica.CreateClient();
        var tokens = await admin.AutenticarComoAdministrador(Ct);

        var resposta = await admin.ComToken(tokens.AccessToken).GetAsync($"/api/v1/arquivos/{arquivo.Id}/conteudo", Ct);

        resposta.EnsureSuccessStatusCode();
        (await resposta.Content.ReadAsStringAsync(Ct)).ShouldBe(Conteudo);
    }

    [Fact]
    public async Task A_listagem_mostra_apenas_os_arquivos_do_proprio_usuario()
    {
        var (cliente, arquivo) = await EnviarComoUsuarioNovo();
        await EnviarComoUsuarioNovo("outro.csv");

        var pagina = await cliente.GetFromJsonAsync<PaginaDTO<ArquivoResumoDTO>>("/api/v1/arquivos", Ct);

        pagina.ShouldNotBeNull();
        pagina.Itens.Count.ShouldBe(1);
        pagina.Itens[0].Id.ShouldBe(arquivo.Id);
    }

    [Fact]
    public async Task A_listagem_filtra_por_categoria()
    {
        var cliente = fabrica.CreateClient();
        var tokens = await cliente.RegistrarUsuarioComum(Ct);
        cliente.ComToken(tokens.AccessToken);

        await cliente.PostAsync("/api/v1/arquivos", Multipart("a.csv", "anexos"), Ct);
        await cliente.PostAsync("/api/v1/arquivos", Multipart("b.csv", "importacoes"), Ct);

        var pagina = await cliente.GetFromJsonAsync<PaginaDTO<ArquivoResumoDTO>>("/api/v1/arquivos?categoria=importacoes", Ct);

        pagina!.Itens.Count.ShouldBe(1);
        pagina.Itens[0].Nome.ShouldBe("b.csv");
    }

    [Fact]
    public async Task Remover_apaga_o_registro_e_o_conteudo()
    {
        var (cliente, arquivo) = await EnviarComoUsuarioNovo();

        var remocao = await cliente.DeleteAsync($"/api/v1/arquivos/{arquivo.Id}", Ct);
        remocao.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var depois = await cliente.GetAsync($"/api/v1/arquivos/{arquivo.Id}/conteudo", Ct);
        depois.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Um_usuario_nao_remove_arquivo_de_outro()
    {
        var (dono, arquivo) = await EnviarComoUsuarioNovo();

        var intruso = fabrica.CreateClient();
        var tokens = await intruso.RegistrarUsuarioComum(Ct);

        var remocao = await intruso.ComToken(tokens.AccessToken).DeleteAsync($"/api/v1/arquivos/{arquivo.Id}", Ct);
        remocao.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var aindaBaixa = await dono.GetAsync($"/api/v1/arquivos/{arquivo.Id}/conteudo", Ct);
        aindaBaixa.EnsureSuccessStatusCode();
    }
}
