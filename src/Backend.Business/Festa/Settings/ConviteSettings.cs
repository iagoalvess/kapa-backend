namespace Backend.Business.Festa.Settings;

/// <summary>
/// O segredo que assina o código do convite da festa.
/// </summary>
/// <remarks>
/// <b>Persistido, e não sorteado na subida</b> (decisão 5): a URL temporária do acervo morre num
/// reinício de propósito, mas um convite impresso em outubro precisa valer em dezembro, depois de
/// vinte deploys. Mora no cofre, ao lado da chave de dados — e, como ela, <b>rotacionar invalida todos
/// os convites emitidos</b>: a assinatura de cada um deixa de bater.
/// </remarks>
public sealed class ConviteSettings
{
    /// <summary>Nome da seção correspondente no arquivo de configuração.</summary>
    public const string Secao = "Festa";

    /// <summary>
    /// Chave do HMAC, em Base64, com 32 bytes ou mais. Gere com <c>openssl rand -base64 32</c>.
    /// </summary>
    public string SegredoDoConvite { get; init; } = string.Empty;

    /// <summary>Se a chave decodifica em pelo menos 32 bytes.</summary>
    public bool SegredoValido()
    {
        var destino = new byte[(SegredoDoConvite.Length * 3 / 4) + 3];

        return Convert.TryFromBase64String(SegredoDoConvite, destino, out var escritos) && escritos >= 32;
    }
}
