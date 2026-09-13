using Backend.Business.Abstractions;
using SkiaSharp;

namespace Backend.Business.Formandos.Services;

/// <summary>
/// Confere e prepara a foto do formando: uma imagem, um tamanho, sem editor.
/// </summary>
/// <remarks>
/// O tipo é decidido pelos primeiros bytes (magic bytes), nunca pela extensão nem pelo
/// <c>Content-Type</c> — os dois são texto que o cliente escolhe, e um <c>.exe</c> renomeado para
/// <c>.jpg</c> passaria pelos dois.
/// <para>
/// A imagem sai reencodada como JPEG de no máximo <see cref="Lado"/>×<see cref="Lado"/>: o que
/// é gravado é sempre o que o servidor produziu, e não os bytes que chegaram — metadado EXIF com
/// GPS da casa do aluno some junto.
/// </para>
/// <para>
/// <c>ponytail:</c> sem crop interativo; o cliente mostra com <c>object-fit: cover</c>. Crop com
/// biblioteca de canvas quando alguém reclamar da foto cortada.
/// </para>
/// </remarks>
public static class FotoDoFormando
{
    /// <summary>Teto do envio, em megabytes.</summary>
    public const int TamanhoMaximoEmMB = 5;

    /// <summary>Lado máximo da imagem gravada, em pixels.</summary>
    public const int Lado = 512;

    /// <summary>
    /// Teto de pixels da imagem de origem.
    /// </summary>
    /// <remarks>
    /// Um PNG de 5 MB pode declarar 30.000×30.000 pixels e pedir gigabytes de memória ao ser
    /// decodificado. 50 MP cobre a câmera de qualquer celular.
    /// </remarks>
    private const long PixelsMaximos = 50_000_000;

    private const int QualidadeDoJpeg = 85;

    private static readonly Erro Ilegivel = Erro.Validacao("perfil.foto_ilegivel", "Não foi possível ler esta imagem. Envie outra foto.", "foto");

    /// <summary>Teto do envio, em bytes.</summary>
    public static long TamanhoMaximoEmBytes => TamanhoMaximoEmMB * 1024L * 1024L;

    /// <summary>
    /// Confere tamanho e tipo e devolve o JPEG final.
    /// </summary>
    /// <remarks>O tamanho é conferido antes de ler um byte do conteúdo.</remarks>
    /// <param name="conteudo">Bytes enviados. Quem chama é dono do descarte.</param>
    /// <param name="tamanho">Tamanho do envio, em bytes.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O JPEG pronto para gravar, ou o erro de validação.</returns>
    public static async Task<Result<byte[]>> Preparar(Stream conteudo, long tamanho, CancellationToken ct = default)
    {
        if (tamanho <= 0)
            return Erro.Validacao("perfil.foto_vazia", "Nenhuma imagem foi enviada.", "foto");

        if (tamanho > TamanhoMaximoEmBytes)
            return Erro.Validacao("perfil.foto_grande", $"A foto excede o limite de {TamanhoMaximoEmMB} MB.", "foto");

        using var memoria = new MemoryStream();
        await conteudo.CopyToAsync(memoria, ct);

        if (memoria.Length > TamanhoMaximoEmBytes)
            return Erro.Validacao("perfil.foto_grande", $"A foto excede o limite de {TamanhoMaximoEmMB} MB.", "foto");

        var bytes = memoria.ToArray();

        if (!TipoPermitido(bytes))
            return Erro.Validacao("perfil.foto_tipo_invalido", "Envie uma imagem JPEG, PNG ou WebP.", "foto");

        using var dados = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(dados);

        if (codec is null || (long)codec.Info.Width * codec.Info.Height > PixelsMaximos)
            return Ilegivel;

        var jpeg = Redimensionar(codec);

        return jpeg is null ? Ilegivel : jpeg;
    }

    /// <summary>JPEG, PNG ou WebP pelos primeiros bytes.</summary>
    /// <param name="bytes">Conteúdo enviado.</param>
    public static bool TipoPermitido(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])
        || bytes.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
        || (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8));

    /// <summary>
    /// Decodifica já reduzida, desenha na orientação certa e reencoda.
    /// </summary>
    /// <remarks>
    /// O JPEG reduz em 1/8, 1/4 e 1/2 na própria decodificação: a foto de 12 MP nunca ocupa a
    /// memória inteira, e a redução restante é pequena o bastante para a interpolação cúbica não
    /// serrilhar. A escala é a <b>menor que ainda cobre</b> o lado final — pedir a mais próxima
    /// deixava uma foto de 3200 px em 400 px (1/8), abaixo dos 512, e ela nunca é ampliada.
    /// PNG e WebP não reduzem na leitura e vêm inteiros.
    /// <para>
    /// Foto de celular vem deitada com a orientação no EXIF; ignorá-la deixaria o rosto de lado.
    /// </para>
    /// </remarks>
    /// <param name="codec">Decodificador da imagem enviada.</param>
    private static byte[]? Redimensionar(SKCodec codec)
    {
        var lida = codec.Info.Size;

        foreach (var fracao in (float[])[0.125f, 0.25f, 0.5f])
        {
            var candidata = codec.GetScaledDimensions(fracao);

            if (Math.Max(candidata.Width, candidata.Height) >= Lado)
            {
                lida = candidata;
                break;
            }
        }

        using var original = SKBitmap.Decode(codec, codec.Info.WithSize(lida.Width, lida.Height));

        if (original is null)
            return null;

        var origem = codec.EncodedOrigin;
        var deitada = origem is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (largura, altura) = deitada ? (original.Height, original.Width) : (original.Width, original.Height);
        var escala = Math.Min(1f, (float)Lado / Math.Max(largura, altura));

        var destino = new SKImageInfo(Math.Max(1, (int)Math.Round(largura * escala)), Math.Max(1, (int)Math.Round(altura * escala)));

        using var superficie = SKSurface.Create(destino);
        var tela = superficie.Canvas;

        tela.Clear(SKColors.White);
        tela.Scale(escala);
        Orientar(tela, origem, original.Width, original.Height);

        using var imagem = SKImage.FromBitmap(original);
        tela.DrawImage(imagem, 0, 0, new SKSamplingOptions(SKCubicResampler.Mitchell));

        using var final = superficie.Snapshot();
        using var jpeg = final.Encode(SKEncodedImageFormat.Jpeg, QualidadeDoJpeg);

        return jpeg.ToArray();
    }

    /// <summary>
    /// Gira a tela para a imagem crua sair na orientação do EXIF.
    /// </summary>
    /// <remarks>
    /// <c>ponytail:</c> só as rotações (3, 6 e 8), que é o que câmera de celular grava. As
    /// orientações espelhadas (2, 4, 5, 7) saem como vieram — entram aqui se alguém mandar uma.
    /// </remarks>
    /// <param name="tela">Tela já escalada.</param>
    /// <param name="origem">Orientação gravada no arquivo.</param>
    /// <param name="largura">Largura da imagem crua.</param>
    /// <param name="altura">Altura da imagem crua.</param>
    private static void Orientar(SKCanvas tela, SKEncodedOrigin origem, int largura, int altura)
    {
        switch (origem)
        {
            case SKEncodedOrigin.BottomRight:
                tela.Translate(largura, altura);
                tela.RotateDegrees(180);
                break;
            case SKEncodedOrigin.RightTop:
                tela.Translate(altura, 0);
                tela.RotateDegrees(90);
                break;
            case SKEncodedOrigin.LeftBottom:
                tela.Translate(0, largura);
                tela.RotateDegrees(270);
                break;
        }
    }
}
