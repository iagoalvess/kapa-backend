using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Business.Festa.Settings;
using Microsoft.Extensions.Options;

namespace Backend.Business.Common;

/// <summary>
/// O link que o e-mail de quem pede leva para confirmar uma mudança sensível — trocar para onde vai o dinheiro
/// da turma, ou dar o papel de Presidente (revisão de segurança de 05/10/2026).
/// </summary>
/// <remarks>
/// <c>{dados}.{assinatura}</c>, sem nada no banco: os dados da mudança vão dentro, em JSON, com o prazo, e o HMAC
/// impede que se troque uma vírgula. A sessão do presidente sozinha não basta mais para desviar a mensalidade:
/// quem roubou a senha ou o cookie precisa também da caixa de e-mail.
/// <para>
/// Uso único sem estado: quem lê confere que o mundo ainda está como estava no pedido — a conta com os mesmos
/// meios, o membro com o mesmo papel. Aplicada a mudança, o "antes" deixou de ser verdade, e o mesmo link morre.
/// </para>
/// <para>
/// A chave é o segredo do convite da festa, derivada por HKDF com rótulo próprio, como a do
/// <see cref="Marketing.Services.LinkDeDescadastro"/>; cada finalidade entra na assinatura, e o link de uma não
/// serve na outra.
/// </para>
/// </remarks>
/// <param name="convite">O segredo de onde a chave é derivada.</param>
public sealed class ConfirmacaoPorEmail(IOptions<ConviteSettings> convite)
{
    /// <summary>Por quanto tempo o link vale.</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(30);

    private readonly Lazy<byte[]> _chave = new(() =>
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            convite.Value.SegredoValido()
                ? Convert.FromBase64String(convite.Value.SegredoDoConvite)
                : throw new InvalidOperationException(
                    $"'{ConviteSettings.Secao}:SegredoDoConvite' precisa ser uma chave de 32 bytes ou mais em Base64."
                ),
            32,
            info: "kapa:confirmacao-por-email"u8.ToArray()
        )
    );

    /// <summary>Um token para os dados, valendo <see cref="Validade"/> a partir de agora.</summary>
    /// <param name="finalidade">O que o link confirma — entra na assinatura.</param>
    /// <param name="dados">A mudança pedida.</param>
    /// <param name="agoraUtc">Momento do pedido.</param>
    public string Assinar<T>(string finalidade, T dados, DateTime agoraUtc)
    {
        var envelope = new Envelope<T>(new DateTimeOffset(agoraUtc.Add(Validade), TimeSpan.Zero).ToUnixTimeSeconds(), dados);
        var corpo = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(envelope));

        return $"{corpo}.{Assinatura(finalidade, corpo)}";
    }

    /// <summary>Os dados de um token íntegro, desta finalidade e no prazo; nulo em qualquer outro caso.</summary>
    /// <param name="finalidade">O que o link deveria confirmar.</param>
    /// <param name="token">O que veio na requisição.</param>
    /// <param name="agoraUtc">Instante de referência.</param>
    public T? Ler<T>(string finalidade, string? token, DateTime agoraUtc)
        where T : class
    {
        if (token?.Split('.') is not [var corpo, var assinatura])
            return null;

        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Assinatura(finalidade, corpo)), Encoding.ASCII.GetBytes(assinatura)))
            return null;

        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope<T>>(Base64Url.DecodeFromChars(corpo));

            return envelope is { Dados: not null } && envelope.Expira > new DateTimeOffset(agoraUtc, TimeSpan.Zero).ToUnixTimeSeconds()
                ? envelope.Dados
                : null;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Impressão digital de um estado — o "antes" que o token carrega para morrer depois de usado.
    /// </summary>
    /// <param name="estado">O estado como está agora; nulo quando ainda não existe.</param>
    public static string Impressao<T>(T? estado) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(estado)));

    private string Assinatura(string finalidade, string corpo) =>
        Base64Url.EncodeToString(HMACSHA256.HashData(_chave.Value, Encoding.UTF8.GetBytes($"{finalidade}:{corpo}")));

    private sealed record Envelope<T>(long Expira, T? Dados);
}
