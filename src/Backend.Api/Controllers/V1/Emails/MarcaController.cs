using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Business.Common.Pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace Backend.Api.Controllers.V1.Emails;

/// <summary>
/// As imagens da marca que os e-mails referenciam por URL.
/// </summary>
/// <remarks>
/// Anônima e pública: quem busca é o proxy de imagens do Gmail ou o cliente de e-mail, sem sessão. Serve
/// os PNGs embutidos no assembly (<see cref="RecursosDaMarca"/>), a mesma fonte do PDF — não o
/// armazenamento de arquivos, que é privado e só entrega por URL que vence em minutos, e o e-mail é aberto
/// meses depois.
/// <para>
/// Cache de um ano, <c>immutable</c>: a URL leva a versão do conteúdo (<c>?v=</c>), então desenho novo é
/// URL nova. O PNG atual responde em qualquer versão, para o e-mail antigo nunca abrir com a imagem
/// quebrada. A Cloudflare, na frente da API, guarda a resposta e faz o papel de CDN.
/// </para>
/// </remarks>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/marca")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitConfig.Vitrine)]
public sealed class MarcaController : MainController
{
    /// <summary>Um PNG da marca: <c>logo</c> ou um mascote, em minúsculas.</summary>
    /// <param name="nome">Nome do arquivo, sem extensão.</param>
    [HttpGet("{nome:regex(^[[a-z]]+$)}.png")]
    [Produces("image/png")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Obter(string nome)
    {
        if (RecursosDaMarca.Abrir(nome) is not { } conteudo)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        return File(conteudo, "image/png", lastModified: null, entityTag: new EntityTagHeaderValue($"\"{RecursosDaMarca.Versao(nome)}\""));
    }
}
