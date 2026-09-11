using System.Text;
using Backend.Business.Arquivos.Services;
using Backend.Business.Arquivos.Settings;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Backend.UnitTests.Arquivos;

/// <summary>
/// Cobre o provedor local, incluindo a defesa contra travessia de diretório.
/// </summary>
public sealed class ArmazenamentoLocalTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), $"backend-testes-{Guid.CreateVersion7():N}");

    private ArmazenamentoLocal Criar() => new(Options.Create(new ArmazenamentoSettings { CaminhoLocal = _raiz }));

    private static Stream Conteudo(string texto) => new MemoryStream(Encoding.UTF8.GetBytes(texto));

    [Fact]
    public async Task Grava_e_le_de_volta_o_mesmo_conteudo()
    {
        var armazenamento = Criar();

        await armazenamento.GravarAsync("anexos/2026/03/abc.txt", Conteudo("conteúdo do arquivo"), "text/plain", Ct);

        await using var leitura = await armazenamento.AbrirLeituraAsync("anexos/2026/03/abc.txt", Ct);
        using var leitor = new StreamReader(leitura);

        (await leitor.ReadToEndAsync(Ct)).ShouldBe("conteúdo do arquivo");
    }

    [Fact]
    public async Task Cria_os_diretorios_intermediarios_da_chave()
    {
        await Criar().GravarAsync("a/b/c/d/arquivo.txt", Conteudo("x"), "text/plain", Ct);

        File.Exists(Path.Combine(_raiz, "a", "b", "c", "d", "arquivo.txt")).ShouldBeTrue();
    }

    [Fact]
    public async Task Gravar_de_novo_na_mesma_chave_sobrescreve()
    {
        var armazenamento = Criar();

        await armazenamento.GravarAsync("x.txt", Conteudo("primeiro conteúdo longo"), "text/plain", Ct);
        await armazenamento.GravarAsync("x.txt", Conteudo("curto"), "text/plain", Ct);

        await using var leitura = await armazenamento.AbrirLeituraAsync("x.txt", Ct);
        using var leitor = new StreamReader(leitura);

        (await leitor.ReadToEndAsync(Ct)).ShouldBe("curto");
    }

    [Fact]
    public async Task Ler_uma_chave_inexistente_lanca_FileNotFound()
    {
        await Should.ThrowAsync<FileNotFoundException>(() => Criar().AbrirLeituraAsync("nao/existe.txt", Ct));
    }

    [Fact]
    public async Task Remover_uma_chave_inexistente_nao_falha()
    {
        await Should.NotThrowAsync(() => Criar().RemoverAsync("nao/existe.txt", Ct));
    }

    [Fact]
    public async Task Remover_apaga_o_arquivo()
    {
        var armazenamento = Criar();
        await armazenamento.GravarAsync("y.txt", Conteudo("x"), "text/plain", Ct);

        await armazenamento.RemoverAsync("y.txt", Ct);

        File.Exists(Path.Combine(_raiz, "y.txt")).ShouldBeFalse();
    }

    /// <summary>
    /// As chaves são geradas pela aplicação, mas a defesa existe mesmo assim: é a última linha
    /// antes de uma escrita em qualquer lugar do disco.
    /// </summary>
    [Theory]
    [InlineData("../fora.txt")]
    [InlineData("anexos/../../fora.txt")]
    [InlineData("anexos/../../../etc/senha")]
    public async Task Chave_que_escapa_da_raiz_e_recusada(string chave)
    {
        await Should.ThrowAsync<UnauthorizedAccessException>(() => Criar().GravarAsync(chave, Conteudo("x"), "text/plain", Ct));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_raiz))
            Directory.Delete(_raiz, recursive: true);
    }
}
