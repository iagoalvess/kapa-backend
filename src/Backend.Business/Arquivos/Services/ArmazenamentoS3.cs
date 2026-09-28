using System.Net;
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
    /// <remarks>
    /// O corpo vai sem assinatura (<c>UNSIGNED-PAYLOAD</c>), só o cabeçalho é assinado. A assinatura em
    /// blocos, padrão do SDK, o Cloudflare R2 recusa com "not implemented"; a integridade do corpo fica
    /// com o TLS, e o S3 da AWS aceita o mesmo pedido.
    /// </remarks>
    public async Task GravarAsync(string chave, Stream conteudo, string contentType, CancellationToken ct = default)
    {
        var pedido = new PutObjectRequest
        {
            BucketName = _settings.Bucket,
            Key = ComPrefixo(chave),
            InputStream = conteudo,
            ContentType = contentType,
            DisablePayloadSigning = true,
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
        catch (AmazonS3Exception excecao) when (excecao.StatusCode == HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException($"Objeto não encontrado: {chave}", chave, excecao);
        }
    }

    /// <inheritdoc />
    public async Task RemoverAsync(string chave, CancellationToken ct = default) =>
        await cliente.DeleteObjectAsync(_settings.Bucket, ComPrefixo(chave), ct);

    /// <inheritdoc />
    /// <remarks>
    /// Lista e apaga em lotes de até 1.000, o máximo de um <c>DeleteObjects</c>, até a listagem voltar
    /// vazia. Prefixo vazio é recusado: com o prefixo do bucket vazio, apagaria o bucket inteiro. A barra
    /// final impede que <c>formaturas/abc</c> leve junto <c>formaturas/abcd</c>.
    /// </remarks>
    public async Task RemoverPrefixoAsync(string prefixo, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefixo);

        var pedido = new ListObjectsV2Request { BucketName = _settings.Bucket, Prefix = ComPrefixo(prefixo.TrimEnd('/')) + "/" };

        while (true)
        {
            var pagina = await cliente.ListObjectsV2Async(pedido, ct);

            if (pagina.S3Objects is not { Count: > 0 } objetos)
                return;

            await cliente.DeleteObjectsAsync(
                new DeleteObjectsRequest { BucketName = _settings.Bucket, Objects = [.. objetos.Select(o => new KeyVersion { Key = o.Key })] },
                ct
            );
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// URL pré-assinada do S3, com o nome e o tipo sobrescritos na resposta: o objeto é baixado com o
    /// nome original, e não com a chave interna. É o front que segue o redirecionamento, então o
    /// bucket precisa de CORS liberando a origem do app (ver <c>docs/operacao.md</c>).
    /// </remarks>
    public Task<string> GerarUrlTemporariaAsync(string chave, string nome, string contentType, TimeSpan validade) =>
        cliente.GetPreSignedURLAsync(
            new GetPreSignedUrlRequest
            {
                BucketName = _settings.Bucket,
                Key = ComPrefixo(chave),
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(validade),
                ResponseHeaderOverrides =
                {
                    ContentType = contentType,
                    ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(nome)}",
                },
            }
        );

    private string ComPrefixo(string chave) => string.IsNullOrWhiteSpace(_settings.Prefixo) ? chave : $"{_settings.Prefixo.TrimEnd('/')}/{chave}";
}
