using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Backend.Business.Recebimentos.Services;

/// <summary>
/// O <c>state</c> do OAuth: de qual turma e de qual presidente é a autorização, assinado e com prazo.
/// </summary>
/// <remarks>
/// O retorno do Mercado Pago chega ao Kapa sem sessão — é o navegador vindo de outro site. O <c>state</c>
/// é o que liga esse retorno a quem clicou em "Conectar": a turma e o usuário vão dentro, e o HMAC com o
/// <c>client_secret</c> da aplicação impede que alguém monte um <c>state</c> para ligar a conta dele a
/// uma turma alheia. Quinze minutos bastam: o código do Mercado Pago vale dez.
/// </remarks>
public static class EstadoDaConexao
{
    /// <summary>Por quanto tempo o <c>state</c> vale.</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(15);

    /// <summary>Monta o <c>state</c> para a turma e o presidente.</summary>
    /// <param name="formaturaId">Turma que autoriza.</param>
    /// <param name="usuarioId">Quem clicou.</param>
    /// <param name="agoraUtc">Agora.</param>
    /// <param name="chave">Segredo que assina — o <c>client_secret</c>.</param>
    public static string Assinar(Guid formaturaId, Guid usuarioId, DateTime agoraUtc, string chave)
    {
        var dados = new byte[40];
        formaturaId.TryWriteBytes(dados.AsSpan(0, 16));
        usuarioId.TryWriteBytes(dados.AsSpan(16, 16));
        BinaryPrimitives.WriteInt64BigEndian(dados.AsSpan(32), new DateTimeOffset(agoraUtc.Add(Validade)).ToUnixTimeSeconds());

        return $"{Base64Url(dados)}.{Base64Url(Hmac(dados, chave))}";
    }

    /// <summary>A turma e o usuário de um <c>state</c> íntegro e no prazo; nulo para qualquer outro.</summary>
    /// <param name="state">O que voltou do Mercado Pago.</param>
    /// <param name="agoraUtc">Agora.</param>
    /// <param name="chave">Segredo que assinou.</param>
    public static (Guid FormaturaId, Guid UsuarioId)? Ler(string? state, DateTime agoraUtc, string chave)
    {
        if (state?.Split('.') is not [var parteDosDados, var parteDaAssinatura])
            return null;

        byte[] dados;
        byte[] assinatura;

        try
        {
            dados = Convert.FromBase64String(DeBase64Url(parteDosDados));
            assinatura = Convert.FromBase64String(DeBase64Url(parteDaAssinatura));
        }
        catch (FormatException)
        {
            return null;
        }

        if (dados.Length != 40 || !CryptographicOperations.FixedTimeEquals(assinatura, Hmac(dados, chave)))
            return null;

        if (DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(dados.AsSpan(32))).UtcDateTime < agoraUtc)
            return null;

        return (new Guid(dados.AsSpan(0, 16)), new Guid(dados.AsSpan(16, 16)));
    }

    private static byte[] Hmac(byte[] dados, string chave) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(chave), dados);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string DeBase64Url(string texto)
    {
        var base64 = texto.Replace('-', '+').Replace('_', '/');

        return base64.PadRight(base64.Length + (4 - (base64.Length % 4)) % 4, '=');
    }
}
