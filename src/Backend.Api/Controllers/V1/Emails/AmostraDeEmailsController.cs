using Asp.Versioning;
using Backend.Api.Configuration;
using Backend.Api.DTOs.Emails;
using Backend.Business.Abstractions;
using Backend.Business.Emails.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Api.Controllers.V1.Emails;

/// <summary>
/// Enfileira uma amostra de cada e-mail do produto, com dados de exemplo.
/// </summary>
/// <remarks>
/// Ferramenta de desenvolvimento, não endpoint do produto — a montagem mora no <c>AmostraDeEmails</c>.
/// <para>
/// Responde 404 fora de <c>Development</c>, por lista de permissão e não de bloqueio: um staging
/// chamado <c>Homologacao</c> não pode virar "dispare vinte e-mails para qualquer endereço". Em
/// desenvolvimento, <c>Smtp:RedirecionarPara</c> já desvia tudo para a caixa de quem programa.
/// </para>
/// </remarks>
/// <param name="amostra">A montagem da amostra.</param>
/// <param name="ambiente">Ambiente de execução.</param>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/amostra-de-emails")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting(RateLimitConfig.Padrao)]
public sealed class AmostraDeEmailsController(IAmostraDeEmails amostra, IHostEnvironment ambiente) : MainController
{
    /// <summary>Enfileira a amostra inteira para um endereço.</summary>
    /// <param name="para">Quem recebe. Em desenvolvimento o desvio do SMTP manda tudo para a mesma caixa de qualquer jeito.</param>
    /// <returns>Quando a amostra entrou na fila, e para quem.</returns>
    [HttpPost]
    public async Task<IActionResult> Enfileirar([FromQuery] string para, CancellationToken ct)
    {
        if (!ambiente.IsDevelopment())
            return NotFound();

        var resultado = await amostra.Enfileirar(para, ct);

        return Responder(resultado.Map(enfileiradoEm => new AmostraDeEmailsDTO(enfileiradoEm, para)));
    }
}
