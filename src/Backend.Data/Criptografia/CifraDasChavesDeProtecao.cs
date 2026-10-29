using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Data.Criptografia;

/// <summary>
/// Cifra as chaves do DataProtection antes de irem para o banco, com a mesma chave de dados do CPF.
/// </summary>
/// <remarks>
/// As chaves assinam os links de redefinir senha e confirmar e-mail. Em claro na mesma base dos usuários,
/// quem só lesse o banco (um backup vazado) forjaria a redefinição de qualquer conta — leitura virava
/// tomada de conta. Cifradas, falta a <c>Criptografia:ChaveDeDados</c>, que mora no cofre.
/// <para>
/// Chave gravada antes desta classe continua legível: o DataProtection só chama o decifrador no
/// elemento que ele mesmo marcou como cifrado.
/// </para>
/// </remarks>
/// <param name="cifra">Cifra de campo (AES-256-GCM).</param>
public sealed class CifraDasChavesDeProtecao(CifraDeCampo cifra) : IXmlEncryptor, IXmlDecryptor
{
    /// <summary>Usado pelo DataProtection, que monta o decifrador por reflexão.</summary>
    /// <param name="servicos">Contêiner da aplicação.</param>
    public CifraDasChavesDeProtecao(IServiceProvider servicos)
        : this(servicos.GetRequiredService<CifraDeCampo>()) { }

    /// <inheritdoc />
    public EncryptedXmlInfo Encrypt(XElement plaintextElement) =>
        new(new XElement("cifrado", cifra.Cifrar(plaintextElement.ToString(SaveOptions.DisableFormatting))), typeof(CifraDasChavesDeProtecao));

    /// <inheritdoc />
    public XElement Decrypt(XElement encryptedElement) => XElement.Parse(cifra.Decifrar(encryptedElement.Value));
}
