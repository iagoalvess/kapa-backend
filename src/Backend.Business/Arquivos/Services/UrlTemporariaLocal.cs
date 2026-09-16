using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Backend.Business.Arquivos.Models;

namespace Backend.Business.Arquivos.Services;

/// <summary>
/// Assina e confere as URLs temporárias do provedor local — o que o S3 faz com a URL pré-assinada.
/// </summary>
/// <remarks>
/// A URL aponta para um endpoint anônimo da própria API (<see cref="Caminho"/>), que serve o objeto
/// se a assinatura bater e o prazo não tiver passado. Quem autoriza é quem <b>emite</b> a URL; o
/// endpoint só confere que ela saiu daqui e ainda vale.
/// <para>
/// A chave do HMAC é sorteada ao subir o processo, e por isso a URL morre também num reinício. É o
/// provedor de desenvolvimento e teste: uma réplica, um processo. Em produção quem assina é o S3.
/// </para>
/// </remarks>
public sealed class UrlTemporariaLocal
{
    /// <summary>Endpoint que serve o objeto — o <c>ArquivoController</c> responde nele.</summary>
    public const string Caminho = "/api/v1/arquivos/temporario";

    private readonly byte[] _segredo = RandomNumberGenerator.GetBytes(32);

    /// <summary>A URL, relativa à API, que serve o objeto até <paramref name="expiraEm"/>.</summary>
    /// <param name="chave">Chave do objeto.</param>
    /// <param name="nome">Nome para o download.</param>
    /// <param name="tipo">Tipo do conteúdo.</param>
    /// <param name="expiraEm">Até quando vale.</param>
    public string Gerar(string chave, string nome, string tipo, DateTimeOffset expiraEm)
    {
        var expira = expiraEm.ToUnixTimeSeconds();

        return $"{Caminho}?chave={Uri.EscapeDataString(chave)}&nome={Uri.EscapeDataString(nome)}&tipo={Uri.EscapeDataString(tipo)}"
            + $"&expira={expira.ToString(CultureInfo.InvariantCulture)}&assinatura={Assinar(chave, nome, tipo, expira)}";
    }

    /// <summary>Se a URL saiu daqui, ninguém mexeu nela e o prazo ainda não passou.</summary>
    /// <param name="objeto">O que veio na query string.</param>
    /// <param name="agora">Instante de referência.</param>
    public bool Conferir(ObjetoTemporario objeto, DateTimeOffset agora)
    {
        if (
            objeto is not { Chave: { } chave, Nome: { } nome, Tipo: { } tipo, Assinatura: { } assinatura }
            || objeto.Expira <= agora.ToUnixTimeSeconds()
        )
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Assinar(chave, nome, tipo, objeto.Expira)),
            Encoding.ASCII.GetBytes(assinatura)
        );
    }

    private string Assinar(string chave, string nome, string tipo, long expira) =>
        Base64Url.EncodeToString(HMACSHA256.HashData(_segredo, Encoding.UTF8.GetBytes($"{chave}\n{nome}\n{tipo}\n{expira}")));
}
