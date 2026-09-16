using System.Globalization;
using System.Text;
using Backend.Business.Common.Texto;

namespace Backend.Business.Recebimentos.Services;

/// <summary>
/// O "PIX copia-e-cola": o BR Code estático do Banco Central, montado a partir da chave.
/// </summary>
/// <remarks>
/// Padrão EMV: cada campo é tipo (2 dígitos), tamanho (2 dígitos) e valor, e o texto termina com o
/// CRC16 de tudo o que vem antes. Montar não depende de banco nem de API — por isso não há biblioteca,
/// e o teste é o exemplo publicado no manual do BR Code, conferido byte a byte.
/// <para>
/// ponytail: PIX estático não expira nem avisa quando é pago. Baixa automática é PIX dinâmico e
/// webhook, pela conta de um PSP que a própria comissão conecte — pós-lançamento.
/// </para>
/// </remarks>
public static class BrCode
{
    /// <summary>Limite do padrão para o nome do recebedor (campo 59).</summary>
    public const int TamanhoMaximoDoNome = 25;

    /// <summary>Limite do padrão para a cidade do recebedor (campo 60).</summary>
    public const int TamanhoMaximoDaCidade = 15;

    /// <summary>Limite do identificador da transação (campo 62-05) no PIX estático.</summary>
    public const int TamanhoMaximoDoIdentificador = 25;

    /// <summary>Identificador para quando não há: o padrão pede três asteriscos.</summary>
    private const string SemIdentificador = "***";

    /// <summary>
    /// Monta o copia-e-cola.
    /// </summary>
    /// <param name="chave">Chave PIX, no formato do diretório (<c>ChavePix.Normalizar</c>).</param>
    /// <param name="nome">Nome do titular; sai sem acento e cortado em 25.</param>
    /// <param name="cidade">Cidade do titular; sai sem acento e cortada em 15.</param>
    /// <param name="valorEmCentavos">Valor fixo; ausente, quem paga digita.</param>
    /// <param name="identificador">Identificador da cobrança; sai só com letras e números, cortado em 25.</param>
    public static string Montar(string chave, string nome, string cidade, long? valorEmCentavos = null, string? identificador = null)
    {
        var conta = Campo("00", "br.gov.bcb.pix") + Campo("01", chave);
        var valor = valorEmCentavos is { } centavos ? Campo("54", (centavos / 100m).ToString("0.00", CultureInfo.InvariantCulture)) : string.Empty;

        var semCrc =
            Campo("00", "01")
            + Campo("26", conta)
            + Campo("52", "0000")
            + Campo("53", "986")
            + valor
            + Campo("58", "BR")
            + Campo("59", Texto(nome, TamanhoMaximoDoNome))
            + Campo("60", Texto(cidade, TamanhoMaximoDaCidade))
            + Campo("62", Campo("05", Identificador(identificador)))
            + "6304";

        return semCrc + Crc16(semCrc).ToString("X4", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// O texto como o padrão aceita: sem acento, só ASCII imprimível, cortado no limite.
    /// </summary>
    /// <remarks>Público porque o validator pergunta se sobra alguma coisa depois disso.</remarks>
    /// <param name="texto">Texto como a pessoa digitou.</param>
    /// <param name="maximo">Limite do campo.</param>
    public static string Texto(string? texto, int maximo)
    {
        var ascii = new string([.. TextoUtils.SemAcento(texto).Where(c => c is >= ' ' and <= '~')]).Trim();

        return (ascii.Length <= maximo ? ascii : ascii[..maximo]).TrimEnd();
    }

    private static string Identificador(string? identificador)
    {
        var alfanumerico = new string([.. (identificador ?? string.Empty).Where(char.IsAsciiLetterOrDigit)]);

        return alfanumerico.Length == 0 ? SemIdentificador : alfanumerico[..Math.Min(alfanumerico.Length, TamanhoMaximoDoIdentificador)];
    }

    private static string Campo(string id, string valor) => $"{id}{valor.Length:D2}{valor}";

    /// <summary>CRC16-CCITT-FALSE (polinômio 0x1021, início 0xFFFF), o que o manual do BR Code pede.</summary>
    /// <param name="texto">Tudo até o <c>6304</c>, inclusive.</param>
    private static ushort Crc16(string texto)
    {
        ushort crc = 0xFFFF;

        foreach (var octeto in Encoding.ASCII.GetBytes(texto))
        {
            crc ^= (ushort)(octeto << 8);

            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }

        return crc;
    }
}
