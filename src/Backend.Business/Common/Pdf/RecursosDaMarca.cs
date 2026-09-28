using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Backend.Business.Common.Pdf;

/// <summary>
/// Os PNGs da marca — logo e mascotes —, que o e-mail referencia e o PDF embute.
/// </summary>
/// <remarks>
/// Embutidos no assembly (<c>Emails/Recursos</c>), porque API e Worker são processos diferentes e os
/// dois precisam deles. É a fonte única: o PDF embute os bytes, e o e-mail aponta para a rota pública da
/// API que serve estes mesmos bytes (<c>MarcaController</c>). Imagem da marca é código, versionada com o
/// deploy — não vai para o armazenamento de arquivos, que é dos usuários.
/// </remarks>
public static class RecursosDaMarca
{
    private static readonly ConcurrentDictionary<string, string?> Versoes = new(StringComparer.Ordinal);

    /// <summary>O PNG pelo nome do arquivo, sem extensão; nulo se ele não existir.</summary>
    /// <param name="nome">Nome do arquivo — <c>logo</c> ou o mascote em minúsculas.</param>
    public static Stream? Abrir(string nome) =>
        typeof(RecursosDaMarca).Assembly.GetManifestResourceStream($"Backend.Business.Emails.Recursos.{nome}.png");

    /// <summary>
    /// Os 8 primeiros caracteres do SHA-256 do PNG; nulo se ele não existir.
    /// </summary>
    /// <remarks>
    /// Vai na URL da imagem (<c>?v=</c>), que é servida com cache de um ano. Trocou o desenho, muda a
    /// versão, e o e-mail novo aponta para uma URL que nenhum cache conhece — o padrão de <i>cache
    /// busting</i> de todo CDN. O e-mail antigo continua abrindo: a rota serve o PNG atual em qualquer
    /// versão.
    /// </remarks>
    /// <param name="nome">Nome do arquivo, sem extensão.</param>
    public static string? Versao(string nome) =>
        Versoes.GetOrAdd(
            nome,
            chave =>
            {
                using var conteudo = Abrir(chave);

                return conteudo is null ? null : Convert.ToHexStringLower(SHA256.HashData(conteudo))[..8];
            }
        );
}
