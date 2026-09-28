using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Backend.Business.Common.Texto;
using Backend.Business.Festa.Settings;
using Microsoft.Extensions.Options;

namespace Backend.Business.Festa.Services;

/// <summary>
/// O código do convite da festa: sorteio, alfabeto e a assinatura que o acompanha na URL.
/// </summary>
/// <remarks>
/// <b>Código</b> é <c>MED27-7QK4</c> (decisão 4): o prefixo da turma e quatro caracteres sorteados,
/// num alfabeto sem <c>0</c>, <c>O</c>, <c>1</c>, <c>I</c> e <c>L</c> — curto para ditar na porta,
/// aleatório para não ser adivinhado. <b>Token</b> é o código com oito caracteres de assinatura HMAC
/// (<c>MED27-7QK4-XK3P9QHA</c>): é o que vai na URL e no QR, e é o que deixa reconhecer um convite
/// falsificado <b>sem consultar o banco</b> (decisão 5).
/// <para>
/// <c>ponytail:</c> nada de par de chaves nem de JWT no QR. HMAC com segredo no cofre e oito
/// caracteres de assinatura; o banco decide o resto quando houver rede.
/// </para>
/// <para>
/// A chave é lida no primeiro uso, como a de <c>CifraDeCampo</c>: o Worker e a CLI do EF montam o
/// container sem precisar dela. Quem garante que a API não sobe sem ela é o <c>ValidateOnStart</c>.
/// </para>
/// </remarks>
/// <param name="options">O segredo configurado.</param>
public sealed class CodigoDoConvite(IOptions<ConviteSettings> options)
{
    /// <summary>
    /// Os caracteres que podem sair no sorteio: dígitos de 2 a 9 e letras sem <c>I</c>, <c>L</c> e
    /// <c>O</c> — 31 ao todo, nenhum que se confunda com outro ao ser lido em voz alta ou num print.
    /// </summary>
    public const string Alfabeto = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>Quantos caracteres sorteados o código tem: 31⁴, quase um milhão por turma.</summary>
    public const int TamanhoDoSorteio = 4;

    /// <summary>Quantos caracteres de assinatura o token leva.</summary>
    public const int TamanhoDaAssinatura = 8;

    private readonly Lazy<byte[]> _chave = new(() =>
        options.Value.SegredoValido()
            ? Convert.FromBase64String(options.Value.SegredoDoConvite)
            : throw new InvalidOperationException($"'{ConviteSettings.Secao}:SegredoDoConvite' precisa ser uma chave de 32 bytes ou mais em Base64.")
    );

    /// <summary>
    /// O prefixo da turma: três letras do curso e os dois dígitos do ano — <c>MED27</c>.
    /// </summary>
    /// <remarks>Sem acento e só letras; curso de menos de três letras completa com <c>X</c>.</remarks>
    /// <param name="curso">Curso da turma.</param>
    /// <param name="ano">Ano de conclusão.</param>
    public static string Prefixo(string curso, int ano)
    {
        var letras = new string([.. TextoUtils.SemAcento(curso).Where(char.IsAsciiLetter).Select(char.ToUpperInvariant).Take(3)]);

        return $"{letras.PadRight(3, 'X')}{(ano % 100).ToString("D2", CultureInfo.InvariantCulture)}";
    }

    /// <summary>Um código novo: o prefixo e quatro caracteres sorteados com gerador criptográfico.</summary>
    /// <param name="prefixo">Prefixo da turma.</param>
    public static string Sortear(string prefixo)
    {
        Span<char> sorteio = stackalloc char[TamanhoDoSorteio];

        for (var i = 0; i < sorteio.Length; i++)
            sorteio[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];

        return $"{prefixo}-{sorteio}";
    }

    /// <summary>O token do código: ele mesmo, um hífen e a assinatura.</summary>
    /// <param name="codigo">Código gravado.</param>
    public string Token(string codigo) => $"{codigo}-{Assinar(codigo)}";

    /// <summary>
    /// O código contido no token, se a assinatura bate; nulo se não bate ou se o formato é outro.
    /// </summary>
    /// <remarks>
    /// Não toca no banco: é o que faz a página pública e a portaria recusarem convite inventado sem
    /// gastar consulta. Comparação em tempo constante, como a URL temporária do acervo.
    /// </remarks>
    /// <param name="token">O que veio na rota.</param>
    public string? Conferir(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var limpo = token.Trim().ToUpperInvariant();
        var separador = limpo.LastIndexOf('-');

        if (separador <= 0 || limpo.Length - separador - 1 != TamanhoDaAssinatura)
            return null;

        var codigo = limpo[..separador];

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Assinar(codigo)), Encoding.ASCII.GetBytes(limpo[(separador + 1)..]))
            ? codigo
            : null;
    }

    /// <summary>
    /// O código que a portaria procura: o do token assinado, ou o digitado à mão.
    /// </summary>
    /// <remarks>
    /// O digitado vem sem assinatura — é o fallback do convidado com o print apagado (decisão 3), e
    /// quem digita é a Gestão logada, dentro da própria turma. Token com assinatura errada continua
    /// sendo recusado: assinatura presente e inválida é adulteração, não esquecimento.
    /// </remarks>
    /// <param name="texto">Código digitado ou token lido.</param>
    public string? ParaPortaria(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var limpo = texto.Trim().ToUpperInvariant();

        return limpo.Count(caractere => caractere == '-') switch
        {
            1 => limpo,
            2 => Conferir(limpo),
            _ => null,
        };
    }

    /// <summary>
    /// Oito caracteres do alfabeto, tirados do HMAC-SHA256 do código.
    /// </summary>
    /// <remarks>
    /// Um byte do HMAC por caractere, reduzido ao alfabeto: perto de 40 bits, que é o que uma
    /// assinatura curta precisa para a adivinhação cair no limite de taxa muito antes de acertar.
    /// </remarks>
    private string Assinar(string codigo)
    {
        var hmac = HMACSHA256.HashData(_chave.Value, Encoding.UTF8.GetBytes(codigo));
        Span<char> assinatura = stackalloc char[TamanhoDaAssinatura];

        for (var i = 0; i < assinatura.Length; i++)
            assinatura[i] = Alfabeto[hmac[i] % Alfabeto.Length];

        return assinatura.ToString();
    }
}
