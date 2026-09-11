using Amazon.S3;
using Amazon.S3.Model;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Settings;
using Microsoft.Extensions.Options;

namespace Backend.Business.Arquivos.Services;

/// <summary>
/// Guarda os arquivos no Amazon S3 ou em serviço compatível.
/// </summary>
/// <remarks>
/// Funciona com MinIO, Cloudflare R2 e Backblaze B2 configurando <c>Armazenamento:S3:ServiceUrl</c>.
/// <para>
/// As credenciais vêm da cadeia padrão do SDK — perfil de instância, role do IRSA, ou as
/// variáveis de ambiente da AWS. Não há campo de chave na configuração da aplicação.
/// </para>
/// </remarks>
/// <param name="cliente">Cliente do S3, registrado na injeção de dependência.</param>
/// <param name="options">Configuração de armazenamento.</param>
public sealed class ArmazenamentoS3(IAmazonS3 cliente, IOptions<ArmazenamentoSettings> options) : IArmazenamentoDeArquivos
{
    private readonly S3Settings _settings = options.Value.S3;

    /// <inheritdoc />
    public async Task GravarAsync(string chave, Stream conteudo, string contentType, CancellationToken ct = default)
    {
        var pedido = new PutObjectRequest
        {
            BucketName = _settings.Bucket,
            Key = ComPrefixo(chave),
            InputStream = conteudo,
            ContentType = contentType,
        };

        await cliente.PutObjectAsync(pedido, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Traduz o 404 do S3 em <see cref="FileNotFoundException"/>, a mesma exceção do provedor
    /// local. É o que permite ao service tratar "objeto sumiu" sem saber qual provedor está ativo.
    /// </remarks>
    public async Task<Stream> AbrirLeituraAsync(string chave, CancellationToken ct = default)
    {
        try
        {
            var resposta = await cliente.GetObjectAsync(_settings.Bucket, ComPrefixo(chave), ct);

            return resposta.ResponseStream;
        }
        catch (AmazonS3Exception excecao) when (excecao.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException($"Objeto não encontrado: {chave}", chave, excecao);
        }
    }

    /// <inheritdoc />
    public async Task RemoverAsync(string chave, CancellationToken ct = default) =>
        await cliente.DeleteObjectAsync(_settings.Bucket, ComPrefixo(chave), ct);

    private string ComPrefixo(string chave) => string.IsNullOrWhiteSpace(_settings.Prefixo) ? chave : $"{_settings.Prefixo.TrimEnd('/')}/{chave}";
}
