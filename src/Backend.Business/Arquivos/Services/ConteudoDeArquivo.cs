namespace Backend.Business.Arquivos.Services;

/// <summary>
/// Confere se os primeiros bytes de um arquivo são do tipo que a extensão promete.
/// </summary>
/// <remarks>
/// Extensão e <c>Content-Type</c> são texto que o cliente escolhe: um executável renomeado para
/// <c>.pdf</c> passa pelos dois. Os primeiros bytes (magic bytes) não mentem tão fácil — e o
/// arquivo que não confere é recusado antes de chegar ao provedor.
/// <para>
/// A pergunta que se responde aqui é "o conteúdo <b>contradiz</b> a extensão?", e não "esta
/// extensão é aceita?" — quem decide o que entra é a lista de permissão de
/// <c>Armazenamento:ExtensoesPermitidas</c>. Formato de texto puro (<c>.csv</c>, <c>.txt</c>) não
/// tem assinatura para contradizer e passa; extensão que este arquivo não conhece é recusada, de
/// propósito: acrescentar uma na lista de permissão sem acrescentá-la aqui deve falhar alto, no
/// primeiro envio, e não em silêncio.
/// </para>
/// <para>
/// ponytail: <c>.docx</c> e <c>.xlsx</c> são ZIP, e aqui só se confere que são ZIP — um ZIP qualquer
/// renomeado passa. Abrir o pacote e procurar o <c>[Content_Types].xml</c> fecha isso, se um dia
/// importar.
/// </para>
/// </remarks>
public static class ConteudoDeArquivo
{
    /// <summary>Quantos bytes do início bastam para qualquer assinatura daqui.</summary>
    private const int BytesLidos = 16;

    /// <summary>Se o início do conteúdo é do tipo da extensão.</summary>
    /// <param name="extensao">Extensão com ponto, em qualquer caixa (<c>.PDF</c>).</param>
    /// <param name="inicio">Primeiros bytes do arquivo.</param>
    public static bool Confere(string extensao, ReadOnlySpan<byte> inicio) =>
        extensao.ToLowerInvariant() switch
        {
            ".pdf" => inicio.StartsWith("%PDF-"u8),
            ".png" => inicio.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            ".jpg" or ".jpeg" => inicio.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]),
            ".webp" => inicio.Length >= 12 && inicio[..4].SequenceEqual("RIFF"u8) && inicio[8..12].SequenceEqual("WEBP"u8),
            ".gif" => inicio.StartsWith("GIF87a"u8) || inicio.StartsWith("GIF89a"u8),
            ".docx" or ".xlsx" or ".zip" => inicio.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x03, 0x04]),
            ".csv" or ".txt" => !inicio.IsEmpty,
            _ => false,
        };

    /// <summary>
    /// Lê o início do fluxo, confere e devolve o fluxo ao começo.
    /// </summary>
    /// <remarks>
    /// Exige fluxo posicionável — o de um <c>IFormFile</c> é. Um que não seja estoura
    /// <see cref="NotSupportedException"/>: é bug de quem chamou, não arquivo inválido.
    /// </remarks>
    /// <param name="nome">Nome do arquivo, de onde sai a extensão.</param>
    /// <param name="conteudo">Fluxo com os bytes, no começo.</param>
    public static async Task<bool> ConfereAsync(string nome, Stream conteudo, CancellationToken ct = default)
    {
        var inicio = new byte[BytesLidos];
        var lidos = await conteudo.ReadAtLeastAsync(inicio, BytesLidos, throwOnEndOfStream: false, ct);

        conteudo.Position = 0;

        return Confere(Path.GetExtension(nome), inicio.AsSpan(0, lidos));
    }
}
