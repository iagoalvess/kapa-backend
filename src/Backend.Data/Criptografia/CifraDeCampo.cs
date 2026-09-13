using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;

namespace Backend.Data.Criptografia;

/// <summary>Chave da cifra de campos sensíveis.</summary>
public sealed class CriptografiaSettings
{
    /// <summary>Nome da seção correspondente no arquivo de configuração.</summary>
    public const string Secao = "Criptografia";

    /// <summary>
    /// Chave AES-256 em Base64 (32 bytes). Gere com <c>openssl rand -base64 32</c>.
    /// </summary>
    /// <remarks>
    /// Mora no secret manager, nunca no repositório. <b>Perdê-la torna ilegível todo CPF gravado</b>;
    /// a rotação vai no runbook de produção.
    /// </remarks>
    public string ChaveDeDados { get; init; } = string.Empty;

    /// <summary>Se a chave decodifica em exatamente 32 bytes.</summary>
    public bool ChaveValida()
    {
        Span<byte> destino = stackalloc byte[33];

        return Convert.TryFromBase64String(ChaveDeDados, destino, out var escritos) && escritos == 32;
    }
}

/// <summary>
/// Cifra de coluna com AES-256-GCM, para dado sensível (CPF).
/// </summary>
/// <remarks>
/// GCM, e não CBC: além de sigilo, autentica — um valor adulterado direto no banco falha ao ler
/// em vez de virar outro CPF. Nonce aleatório por gravação, então o mesmo CPF cifra diferente a
/// cada vez e a coluna não revela quem tem CPF igual a quem — e, pelo mesmo motivo, não admite
/// busca.
/// <para>
/// Formato gravado: Base64 de <c>nonce (12) | tag (16) | texto cifrado</c>.
/// <c>ponytail:</c> sem versão de chave no formato; a rotação, quando existir, reconhece o formato
/// sem prefixo como a versão 1.
/// </para>
/// <para>
/// A chave é lida no primeiro uso, não no construtor: a CLI do EF Core monta o contexto para gerar
/// migration sem precisar da chave. Quem garante que a API não sobe sem ela é o
/// <c>ValidateOnStart</c> em <c>AddData</c>.
/// </para>
/// </remarks>
/// <param name="options">Chave configurada.</param>
public sealed class CifraDeCampo(IOptions<CriptografiaSettings> options)
{
    private const int TamanhoDoNonce = 12;
    private const int TamanhoDaTag = 16;

    private readonly Lazy<byte[]> _chave = new(() =>
        options.Value.ChaveValida()
            ? Convert.FromBase64String(options.Value.ChaveDeDados)
            : throw new InvalidOperationException($"'{CriptografiaSettings.Secao}:ChaveDeDados' precisa ser uma chave de 32 bytes em Base64.")
    );

    /// <summary>Conversor do EF Core que cifra ao gravar e decifra ao ler.</summary>
    /// <remarks>O EF Core não passa nulo a conversor: coluna vazia continua nula, sem cifrar nada.</remarks>
    public ValueConverter<string?, string?> Conversor() => new(texto => Cifrar(texto!), cifrado => Decifrar(cifrado!));

    /// <summary>Cifra o texto.</summary>
    /// <param name="texto">Valor em claro.</param>
    public string Cifrar(string texto)
    {
        var claro = Encoding.UTF8.GetBytes(texto);
        var saida = new byte[TamanhoDoNonce + TamanhoDaTag + claro.Length];
        var nonce = saida.AsSpan(0, TamanhoDoNonce);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_chave.Value, TamanhoDaTag);
        aes.Encrypt(nonce, claro, saida.AsSpan(TamanhoDoNonce + TamanhoDaTag), saida.AsSpan(TamanhoDoNonce, TamanhoDaTag));

        return Convert.ToBase64String(saida);
    }

    /// <summary>Decifra o valor gravado.</summary>
    /// <param name="cifrado">Valor como está na coluna.</param>
    /// <exception cref="AuthenticationTagMismatchException">Se o valor foi adulterado ou a chave é outra.</exception>
    public string Decifrar(string cifrado)
    {
        var entrada = Convert.FromBase64String(cifrado);
        var claro = new byte[entrada.Length - TamanhoDoNonce - TamanhoDaTag];

        using var aes = new AesGcm(_chave.Value, TamanhoDaTag);
        aes.Decrypt(
            entrada.AsSpan(0, TamanhoDoNonce),
            entrada.AsSpan(TamanhoDoNonce + TamanhoDaTag),
            entrada.AsSpan(TamanhoDoNonce, TamanhoDaTag),
            claro
        );

        return Encoding.UTF8.GetString(claro);
    }
}
