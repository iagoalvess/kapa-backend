using System.IO.Compression;
using Backend.Business.Common.Pdf;
using Backend.Business.Emails.Services;
using Shouldly;
using SkiaSharp;

namespace Backend.UnitTests.Common;

/// <summary>
/// O PNG que entra no PDF: pixel a pixel igual ao original, e todo arquivo da marca abre.
/// </summary>
public sealed class ImagemPngTests
{
    /// <summary>
    /// Um PNG gravado por outro codificador volta com os mesmos pixels e a mesma transparência.
    /// </summary>
    /// <remarks>
    /// O Skia escolhe o filtro de cada linha sozinho, e com ruído e degradê usa mais de um — é o que
    /// exercita o desfazer do Sub, do Up, do Average e do Paeth, onde um erro de um byte vira uma
    /// listra na imagem impressa.
    /// </remarks>
    [Fact]
    public void Devolve_os_pixels_e_o_alfa_do_png_original()
    {
        // Arrange
        const int Largura = 37;
        const int Altura = 23;
        var aleatorio = new Random(7);
        using var bitmap = new SKBitmap(new SKImageInfo(Largura, Altura, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        for (var y = 0; y < Altura; y++)
        for (var x = 0; x < Largura; x++)
            bitmap.SetPixel(
                x,
                y,
                new SKColor((byte)(x * 7), (byte)aleatorio.Next(256), (byte)(y * 11), (byte)(x % 5 == 0 ? 255 : aleatorio.Next(256)))
            );

        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100).AsStream();

        // Act
        var imagem = ImagemPng.Ler(png);

        // Assert
        imagem.Largura.ShouldBe(Largura);
        imagem.Altura.ShouldBe(Altura);

        var cor = Inflar(imagem.Cor);
        var alfa = Inflar(imagem.Alfa.ShouldNotBeNull());

        for (var y = 0; y < Altura; y++)
        for (var x = 0; x < Largura; x++)
        {
            var i = (y * Largura) + x;
            var esperado = bitmap.GetPixel(x, y);

            (cor[i * 3], cor[(i * 3) + 1], cor[(i * 3) + 2], alfa[i]).ShouldBe((esperado.Red, esperado.Green, esperado.Blue, esperado.Alpha));
        }
    }

    /// <summary>Todo PNG da marca abre — o arquivo novo em formato que a classe não lê falha aqui, e não no PDF de alguém.</summary>
    /// <param name="nome">O arquivo.</param>
    [Theory]
    [MemberData(nameof(ImagensDaMarca))]
    public void Toda_imagem_da_marca_abre(string nome) => ImagemPng.DaMarca(nome).Alfa.ShouldNotBeNull();

    /// <summary>O logo e cada mascote.</summary>
    public static TheoryData<string> ImagensDaMarca() => [.. Enum.GetValues<Mascote>().Select(ModeloDeEmail.Arquivo).Prepend("logo")];

    private static byte[] Inflar(byte[] comprimido)
    {
        using var saida = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(comprimido), CompressionMode.Decompress))
            zlib.CopyTo(saida);

        return saida.ToArray();
    }
}
