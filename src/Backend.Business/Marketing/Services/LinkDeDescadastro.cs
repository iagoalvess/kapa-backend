using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Backend.Business.Common;
using Backend.Business.Festa.Settings;
using Backend.Business.Marketing.Settings;
using Microsoft.Extensions.Options;

namespace Backend.Business.Marketing.Services;

/// <summary>
/// O token do descadastro de um clique: a conta, até quando vale e uma assinatura HMAC.
/// </summary>
/// <remarks>
/// <c>{usuario}.{expira}.{assinatura}</c> — sem nada no banco. Quem recebe o e-mail sai sem login (pedir login
/// para sair é o que manda a pessoa para o botão de spam), e quem não recebeu não tem como forjar o de outro.
/// <para>
/// A chave é o segredo do convite da festa, derivada por HKDF com rótulo próprio, como a do
/// <see cref="Loja.Services.LinkDaCompra"/>: dois usos, duas chaves, nenhum segredo novo no cofre — e o Worker,
/// que monta o e-mail, já o tem.
/// </para>
/// </remarks>
/// <param name="convite">O segredo de onde a chave é derivada.</param>
/// <param name="comunicacao">Validade do link e endereço da API.</param>
/// <param name="aplicacao">Endereço do app, onde fica a página do descadastro.</param>
public sealed class LinkDeDescadastro(
    IOptions<ConviteSettings> convite,
    IOptions<ComunicacaoDoKapaSettings> comunicacao,
    IOptions<AplicacaoSettings> aplicacao
)
{
    /// <summary>A rota anônima da API que atende o <c>POST</c> do descadastro.</summary>
    public const string CaminhoNaApi = "/api/v1/privacidade/descadastro";

    private readonly Lazy<byte[]> _chave = new(() =>
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            convite.Value.SegredoValido()
                ? Convert.FromBase64String(convite.Value.SegredoDoConvite)
                : throw new InvalidOperationException(
                    $"'{ConviteSettings.Secao}:SegredoDoConvite' precisa ser uma chave de 32 bytes ou mais em Base64."
                ),
            32,
            info: "kapa:descadastro"u8.ToArray()
        )
    );

    /// <summary>Um token novo para a conta, valendo pelos dias configurados a partir de agora.</summary>
    /// <param name="usuarioId">Quem sai ao usar o link.</param>
    /// <param name="agoraUtc">Momento da emissão.</param>
    public string Token(Guid usuarioId, DateTime agoraUtc)
    {
        var expira = new DateTimeOffset(agoraUtc, TimeSpan.Zero).AddDays(comunicacao.Value.DiasDoLinkDeDescadastro).ToUnixTimeSeconds();

        return $"{usuarioId:N}.{expira.ToString(CultureInfo.InvariantCulture)}.{Assinar(usuarioId, expira)}";
    }

    /// <summary>
    /// A conta do token, se a assinatura bate e o prazo não passou; nulo em qualquer outro caso.
    /// </summary>
    /// <remarks>Comparação em tempo constante, como a URL temporária do acervo.</remarks>
    /// <param name="token">O que veio na requisição.</param>
    /// <param name="agoraUtc">Instante de referência.</param>
    public Guid? Conferir(string? token, DateTime agoraUtc)
    {
        if (
            token?.Split('.') is not [var id, var expiraTexto, var assinatura]
            || !Guid.TryParseExact(id, "N", out var usuarioId)
            || !long.TryParse(expiraTexto, NumberStyles.None, CultureInfo.InvariantCulture, out var expira)
            || expira <= new DateTimeOffset(agoraUtc, TimeSpan.Zero).ToUnixTimeSeconds()
        )
            return null;

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Assinar(usuarioId, expira)), Encoding.ASCII.GetBytes(assinatura))
            ? usuarioId
            : null;
    }

    /// <summary>A página "Não quero mais receber" do app, que o rodapé do e-mail abre.</summary>
    /// <param name="token">Token do descadastro.</param>
    public string PaginaNoApp(string token) => aplicacao.Value.MontarUrl(RotasDoFront.Descadastro, [new("token", token)]);

    /// <summary>A URL da API que o cliente de e-mail chama sozinho — a do <c>List-Unsubscribe</c>.</summary>
    /// <param name="token">Token do descadastro.</param>
    public string UrlDaApi(string token) => $"{comunicacao.Value.UrlDaApi.TrimEnd('/')}{CaminhoNaApi}?token={Uri.EscapeDataString(token)}";

    private string Assinar(Guid usuarioId, long expira) =>
        Base64Url.EncodeToString(HMACSHA256.HashData(_chave.Value, Encoding.UTF8.GetBytes($"descadastro:{usuarioId:N}:{expira}")));
}
