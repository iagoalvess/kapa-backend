using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Settings;
using Microsoft.Extensions.Options;

namespace Backend.Business.Arquivos.Services;

/// <summary>
/// Guarda os arquivos no disco do processo.
/// </summary>
/// <remarks>
/// <b>Para desenvolvimento e testes.</b> Em container o disco é efêmero — o arquivo some no
/// próximo deploy — e com mais de uma réplica cada uma enxerga um conjunto diferente. Em
/// produção use <see cref="ArmazenamentoS3"/>.
/// </remarks>
/// <param name="options">Configuração de armazenamento.</param>
public sealed class ArmazenamentoLocal(IOptions<ArmazenamentoSettings> options) : IArmazenamentoDeArquivos
{
    private readonly string _raiz = Path.GetFullPath(options.Value.CaminhoLocal);

    /// <inheritdoc />
    public async Task GravarAsync(string chave, Stream conteudo, string contentType, CancellationToken ct = default)
    {
        var caminho = ResolverCaminho(chave);

        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);

        await using var destino = new FileStream(caminho, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

        await conteudo.CopyToAsync(destino, ct);
    }

    /// <inheritdoc />
    public Task<Stream> AbrirLeituraAsync(string chave, CancellationToken ct = default)
    {
        var caminho = ResolverCaminho(chave);

        if (!File.Exists(caminho))
            throw new FileNotFoundException($"Objeto não encontrado: {chave}", caminho);

        Stream leitura = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);

        return Task.FromResult(leitura);
    }

    /// <inheritdoc />
    public Task RemoverAsync(string chave, CancellationToken ct = default)
    {
        var caminho = ResolverCaminho(chave);

        if (File.Exists(caminho))
            File.Delete(caminho);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Converte a chave em caminho absoluto, garantindo que ele fique dentro da raiz.
    /// </summary>
    /// <remarks>
    /// As chaves são geradas pela aplicação, então em tese não há como sair da raiz. A conferência
    /// existe mesmo assim: é a última linha antes de uma leitura ou escrita em qualquer lugar do
    /// disco, e custa uma comparação de string.
    /// </remarks>
    /// <param name="chave">Caminho do objeto.</param>
    /// <exception cref="UnauthorizedAccessException">Se a chave escapar do diretório raiz.</exception>
    private string ResolverCaminho(string chave)
    {
        var caminho = Path.GetFullPath(Path.Combine(_raiz, chave));

        if (!caminho.StartsWith(_raiz + Path.DirectorySeparatorChar, StringComparison.Ordinal) && caminho != _raiz)
            throw new UnauthorizedAccessException($"A chave '{chave}' aponta para fora do diretório de armazenamento.");

        return caminho;
    }
}
