using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO.Compression;

namespace Backend.Business.Common.Pdf;

/// <summary>
/// Um PNG pronto para entrar no <see cref="DocumentoPdf"/>: a cor e a transparência separadas e já
/// comprimidas, que é como o PDF as quer.
/// </summary>
/// <remarks>
/// O PDF não tem imagem RGBA: a cor vai num objeto e o alfa em outro (a <c>SMask</c>). Por isso o PNG
/// é aberto aqui — inflar, desfazer o filtro de cada linha, separar os canais — em vez de copiado.
/// <para>
/// <c>ponytail:</c> só PNG de 8 bits por canal, RGB ou RGBA, sem entrelaçamento — é o que os PNGs
/// da marca são (<c>Emails/Recursos</c>). Paleta, 16 bits ou entrelaçado lançam, e o teste que abre
/// todos os recursos acusa o arquivo novo que fugir disso antes de ele chegar a um PDF.
/// </para>
/// </remarks>
/// <param name="Largura">Largura em pixels.</param>
/// <param name="Altura">Altura em pixels.</param>
/// <param name="Cor">Os pixels RGB, comprimidos em zlib.</param>
/// <param name="Alfa">A transparência, um byte por pixel e comprimida; nula quando a imagem é toda opaca.</param>
public sealed record ImagemPng(int Largura, int Altura, byte[] Cor, byte[]? Alfa)
{
    private static readonly byte[] Assinatura = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly ConcurrentDictionary<string, ImagemPng> DaMarcaJaLidas = new(StringComparer.Ordinal);

    /// <summary>
    /// Um PNG da marca — o logo ou um mascote —, lido uma vez por processo.
    /// </summary>
    /// <remarks>
    /// Os mesmos arquivos que o e-mail anexa. O PDF leva a imagem dentro dele, então não importa onde
    /// ela mora: o dia em que as imagens forem para um storage, muda só <see cref="RecursosDaMarca"/>.
    /// </remarks>
    /// <param name="nome">Nome do arquivo, sem extensão.</param>
    public static ImagemPng DaMarca(string nome) =>
        DaMarcaJaLidas.GetOrAdd(
            nome,
            static nome =>
            {
                using var png =
                    RecursosDaMarca.Abrir(nome) ?? throw new InvalidOperationException($"A imagem da marca '{nome}' não está no assembly.");

                return Ler(png);
            }
        );

    /// <summary>Abre o PNG.</summary>
    /// <param name="png">O arquivo.</param>
    /// <exception cref="NotSupportedException">Formato fora do que a classe lê.</exception>
    public static ImagemPng Ler(Stream png)
    {
        using var leitor = new BinaryReader(png);

        if (!leitor.ReadBytes(Assinatura.Length).AsSpan().SequenceEqual(Assinatura))
            throw new NotSupportedException("O arquivo não é um PNG.");

        int largura = 0,
            altura = 0,
            canais = 0;
        using var comprimido = new MemoryStream();

        while (true)
        {
            var tamanho = BinaryPrimitives.ReadInt32BigEndian(leitor.ReadBytes(4));
            var tipo = new string(leitor.ReadChars(4));
            var dados = leitor.ReadBytes(tamanho);
            leitor.ReadBytes(4);

            if (tipo == "IHDR")
            {
                largura = BinaryPrimitives.ReadInt32BigEndian(dados);
                altura = BinaryPrimitives.ReadInt32BigEndian(dados.AsSpan(4));
                canais = (Profundidade: dados[8], TipoDeCor: dados[9], Entrelacado: dados[12]) switch
                {
                    (8, 6, 0) => 4,
                    (8, 2, 0) => 3,
                    _ => throw new NotSupportedException("Só PNG de 8 bits, RGB ou RGBA, sem entrelaçamento."),
                };
            }
            else if (tipo == "IDAT")
                comprimido.Write(dados);
            else if (tipo == "IEND")
                break;
        }

        comprimido.Position = 0;
        using var inflado = new MemoryStream();
        using (var zlib = new ZLibStream(comprimido, CompressionMode.Decompress))
            zlib.CopyTo(inflado);

        var pixels = Desfiltrar(inflado.ToArray(), largura, altura, canais);

        return Separar(pixels, largura, altura, canais);
    }

    /// <summary>Desfaz o filtro que o PNG aplica em cada linha (None, Sub, Up, Average, Paeth).</summary>
    private static byte[] Desfiltrar(byte[] dados, int largura, int altura, int canais)
    {
        var porLinha = largura * canais;
        var saida = new byte[porLinha * altura];

        for (var y = 0; y < altura; y++)
        {
            var filtro = dados[y * (porLinha + 1)];
            var origem = (y * (porLinha + 1)) + 1;
            var linha = y * porLinha;

            for (var x = 0; x < porLinha; x++)
            {
                int esquerda = x >= canais ? saida[linha + x - canais] : 0;
                int acima = y > 0 ? saida[linha - porLinha + x] : 0;
                int diagonal = x >= canais && y > 0 ? saida[linha - porLinha + x - canais] : 0;

                var previsto = filtro switch
                {
                    0 => 0,
                    1 => esquerda,
                    2 => acima,
                    3 => (esquerda + acima) / 2,
                    4 => Paeth(esquerda, acima, diagonal),
                    _ => throw new NotSupportedException($"Filtro de linha {filtro} desconhecido."),
                };

                saida[linha + x] = (byte)(dados[origem + x] + previsto);
            }
        }

        return saida;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        return pa <= pb && pa <= pc ? a
            : pb <= pc ? b
            : c;
    }

    /// <summary>Separa RGB e alfa e comprime cada um.</summary>
    private static ImagemPng Separar(byte[] pixels, int largura, int altura, int canais)
    {
        var total = largura * altura;
        var cor = new byte[total * 3];
        var alfa = canais == 4 ? new byte[total] : null;
        var opaca = true;

        for (var i = 0; i < total; i++)
        {
            pixels.AsSpan(i * canais, 3).CopyTo(cor.AsSpan(i * 3));

            if (alfa is null)
                continue;

            alfa[i] = pixels[(i * canais) + 3];
            opaca &= alfa[i] == 255;
        }

        return new ImagemPng(largura, altura, Comprimir(cor), opaca ? null : Comprimir(alfa!));
    }

    private static byte[] Comprimir(byte[] dados)
    {
        using var saida = new MemoryStream();
        using (var zlib = new ZLibStream(saida, CompressionLevel.SmallestSize))
            zlib.Write(dados);

        return saida.ToArray();
    }
}
