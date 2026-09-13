using Backend.Business.Formandos.Services;
using Shouldly;
using SkiaSharp;

namespace Backend.UnitTests.Formandos;

/// <summary>A foto é conferida pelos bytes e sai do servidor em até 512×512.</summary>
public sealed class FotoDoFormandoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Um PNG de verdade, nas dimensões pedidas.</summary>
    public static byte[] Png(int largura, int altura)
    {
        using var bitmap = new SKBitmap(largura, altura);
        bitmap.Erase(SKColors.Orange);

        using var imagem = SKImage.FromBitmap(bitmap);

        return imagem.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    [Fact]
    public async Task Imagem_grande_sai_em_jpeg_dentro_de_512_mantendo_a_proporcao()
    {
        var png = Png(2000, 1000);

        var resultado = await FotoDoFormando.Preparar(new MemoryStream(png), png.Length, Ct);

        using var final = SKBitmap.Decode(resultado.Valor);
        final.Width.ShouldBe(512);
        final.Height.ShouldBe(256);
        resultado.Valor[..3].ShouldBe(new byte[] { 0xFF, 0xD8, 0xFF });
    }

    /// <summary>
    /// O JPEG reduz na decodificação (1/8, 1/4, 1/2). Pegar a escala mais próxima levava 3200 px a
    /// 400 px — abaixo do alvo, e a imagem nunca é ampliada.
    /// </summary>
    [Fact]
    public async Task Jpeg_grande_nao_fica_abaixo_de_512_pela_reducao_na_leitura()
    {
        using var bitmap = new SKBitmap(3200, 2400);
        bitmap.Erase(SKColors.Orange);
        using var imagem = SKImage.FromBitmap(bitmap);
        var jpeg = imagem.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();

        var resultado = await FotoDoFormando.Preparar(new MemoryStream(jpeg), jpeg.Length, Ct);

        using var final = SKBitmap.Decode(resultado.Valor);
        (final.Width, final.Height).ShouldBe((512, 384));
    }

    [Fact]
    public async Task Imagem_pequena_nao_e_ampliada()
    {
        var png = Png(100, 80);

        var resultado = await FotoDoFormando.Preparar(new MemoryStream(png), png.Length, Ct);

        using var final = SKBitmap.Decode(resultado.Valor);
        (final.Width, final.Height).ShouldBe((100, 80));
    }

    /// <summary>A extensão é texto que o cliente escolhe; o que decide são os primeiros bytes.</summary>
    [Fact]
    public async Task Executavel_renomeado_e_recusado_pelos_primeiros_bytes()
    {
        byte[] exe = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00];

        var resultado = await FotoDoFormando.Preparar(new MemoryStream(exe), exe.Length, Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("perfil.foto_tipo_invalido");
    }

    [Fact]
    public async Task Acima_de_5_MB_e_recusada_sem_ler_o_conteudo()
    {
        var conteudo = new FluxoQueNaoPodeSerLido();

        var resultado = await FotoDoFormando.Preparar(conteudo, FotoDoFormando.TamanhoMaximoEmBytes + 1, Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("perfil.foto_grande");
    }

    [Fact]
    public async Task Cabecalho_de_jpeg_com_lixo_depois_e_ilegivel()
    {
        byte[] falso = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x02, 0x03];

        var resultado = await FotoDoFormando.Preparar(new MemoryStream(falso), falso.Length, Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("perfil.foto_ilegivel");
    }

    private sealed class FluxoQueNaoPodeSerLido : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("O conteúdo não devia ter sido lido.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("O conteúdo não devia ter sido lido.");
    }
}
