using System.Buffers.Text;
using System.Text;

namespace Backend.Business.Auth.Services;

/// <summary>
/// Converte os tokens do Identity para um formato seguro em URL, e de volta.
/// </summary>
/// <remarks>
/// Os tokens do Identity são Base64 comum, que contém <c>+</c>, <c>/</c> e <c>=</c> — todos
/// caracteres que mudam de significado dentro de uma query string. Enviar o token cru num link
/// produz o clássico "o link de redefinição não funciona": o <c>+</c> chega como espaço e o token
/// é recusado como inválido.
/// <para>
/// Base64 URL-safe resolve isso de uma vez, sem depender de o cliente lembrar de codificar. Usa
/// <see cref="Base64Url"/>, que é da biblioteca padrão — não exige pacote de ASP.NET aqui na
/// camada de negócio.
/// </para>
/// </remarks>
public static class CodificadorDeToken
{
    /// <summary>Codifica um token do Identity para transportá-lo em URL.</summary>
    /// <param name="token">Token como o Identity o gerou.</param>
    public static string Codificar(string token) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token));

    /// <summary>
    /// Decodifica um token recebido do cliente.
    /// </summary>
    /// <param name="token">Token como veio na requisição.</param>
    /// <returns>O token original, ou nulo se o valor não for um Base64 URL-safe válido.</returns>
    public static string? Decodificar(string token)
    {
        try
        {
            return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
