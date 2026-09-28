using System.Security.Cryptography;
using System.Text;
using Backend.Business.Festa.Settings;
using Backend.Business.Loja.Models;
using Microsoft.Extensions.Options;

namespace Backend.Business.Loja.Services;

/// <summary>
/// O link de acesso à compra: o id da compra e uma assinatura HMAC que leva a versão do link (decisão 10).
/// </summary>
/// <remarks>
/// A decisão 10 falava em token sorteado com o hash no banco. O desvio é deliberado: o e-mail da
/// <b>confirmação</b> precisa levar o link de novo, e com só o hash guardado não haveria como remontá-lo sem
/// matar o que o comprador já tem aberto. Assinado, o link se remonta a qualquer hora, e o banco guarda só a
/// versão — reenviar sobe a versão, e o link anterior deixa de bater, que é o "reenviar mata o anterior".
/// <para>
/// A chave é o segredo do convite da festa, derivada por HKDF com um rótulo próprio: dois usos, duas chaves,
/// e nenhum segredo novo no cofre. Rotacionar o segredo invalida os links, como invalida os convites. A
/// assinatura tem 256 bits — não é o código curto do convite, que se dita na porta: é o que dá acesso ao
/// CPF mascarado e à troca de titular.
/// </para>
/// </remarks>
/// <param name="options">O segredo do convite.</param>
public sealed class LinkDaCompra(IOptions<ConviteSettings> options)
{
    private readonly Lazy<byte[]> _chave = new(() =>
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            options.Value.SegredoValido()
                ? Convert.FromBase64String(options.Value.SegredoDoConvite)
                : throw new InvalidOperationException(
                    $"'{ConviteSettings.Secao}:SegredoDoConvite' precisa ser uma chave de 32 bytes ou mais em Base64."
                ),
            32,
            info: "kapa:link-da-compra"u8.ToArray()
        )
    );

    /// <summary>O token do link: <c>{id}.{assinatura}</c>.</summary>
    /// <param name="compra">Compra, com a versão atual do link.</param>
    public string Token(CompraDeConvite compra) => $"{compra.Id:N}.{Assinar(compra.Id, compra.VersaoDoLink)}";

    /// <summary>O id contido no token, se ele tem a forma certa — a assinatura só se confere com a versão do banco.</summary>
    /// <param name="token">O que veio na rota.</param>
    public static Guid? Id(string? token) =>
        token?.Split('.') is [var id, var assinatura] && assinatura.Length > 0 && Guid.TryParseExact(id, "N", out var compraId) ? compraId : null;

    /// <summary>Se o token é o link vigente desta compra. Comparação em tempo constante.</summary>
    /// <param name="token">O que veio na rota.</param>
    /// <param name="compra">A compra que o id do token apontou.</param>
    public bool Confere(string token, CompraDeConvite compra) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Token(compra)), Encoding.ASCII.GetBytes(token));

    private string Assinar(Guid compraId, int versao)
    {
        var hmac = HMACSHA256.HashData(_chave.Value, Encoding.UTF8.GetBytes($"compra:{compraId:N}:{versao}"));

        return Convert.ToBase64String(hmac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
