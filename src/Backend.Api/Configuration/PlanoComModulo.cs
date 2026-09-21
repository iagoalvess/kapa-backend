using System.Security.Claims;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Auth.Services;
using Microsoft.AspNetCore.Authorization;

namespace Backend.Api.Configuration;

/// <summary>Exige que o plano da formatura inclua o módulo.</summary>
/// <param name="modulo">Código do módulo, de <c>Modulo</c>.</param>
public sealed class PlanoComModuloRequirement(string modulo) : IAuthorizationRequirement
{
    /// <summary>Código do módulo exigido.</summary>
    public string Modulo { get; } = modulo;
}

/// <summary>
/// Confere se o plano contratado pela turma inclui o módulo da área pedida.
/// </summary>
/// <remarks>
/// É o que dá sentido ao plano gratuito: sem isso ele daria o produto inteiro de graça, e a vitrine
/// estaria vendendo módulo que a API libera para todo mundo (o XML de <c>Plano.Modulos</c> dizia, em
/// letras, que era "texto de vitrine, e não regra de acesso" — deixou de ser em 18/09/2026).
/// <para>
/// Vale para leitura <b>e</b> escrita, diferente de <see cref="FormaturaEmStatusHandler"/>: turma
/// suspensa perde a escrita mas continua vendo o que já é dela, enquanto módulo fora do plano é
/// área que a turma nunca comprou — não há o que preservar.
/// </para>
/// <para>Scoped, porque depende do repositório da requisição.</para>
/// </remarks>
/// <param name="assinaturaRepository">Módulos do plano vigente.</param>
public sealed class PlanoComModuloHandler(IAssinaturaRepository assinaturaRepository) : AuthorizationHandler<PlanoComModuloRequirement>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PlanoComModuloRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(TokenService.ClaimDeFormatura), out var formaturaId))
            return;

        var ct = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;

        if (
            await assinaturaRepository.ObterPlanoVigenteDeTodasAsFormaturas(formaturaId, ct) is { } plano
            && plano.Modulos.Contains(requirement.Modulo)
        )
            context.Succeed(requirement);
    }
}

/// <summary>
/// Exige que o plano da turma inclua o módulo: <c>[ExigeModulo(Modulo.Cobrancas)]</c>.
/// </summary>
/// <remarks>
/// Existe porque <c>[Authorize(Policy = Politicas.ExigeModulo(...))]</c> não compila — argumento de
/// atributo tem de ser constante, e o nome da política é montado. Some com um único lugar montando
/// o nome, em vez de uma constante por módulo repetindo a string à mão.
/// </remarks>
/// <param name="modulo">Código do módulo, de <see cref="Modulo"/>.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ExigeModuloAttribute(string modulo) : AuthorizeAttribute(Politicas.ExigeModulo(modulo));
