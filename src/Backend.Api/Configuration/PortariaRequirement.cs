using System.Security.Claims;
using Backend.Business.Auth.Services;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Backend.Api.Configuration;

/// <summary>Exige a Gestão da turma ou um acesso da portaria que continua valendo.</summary>
public sealed class PortariaRequirement : IAuthorizationRequirement;

/// <summary>
/// Confere quem está na porta: o acesso da portaria no banco, ou o papel de Gestão no vínculo gravado.
/// </summary>
/// <remarks>
/// A credencial da portaria traz a claim <c>evento_id</c>, e a da sessão não — é o que separa os dois caminhos.
/// Cortar o acesso vale na requisição seguinte, como rebaixar um papel.
/// <para>Scoped, porque depende dos repositórios da requisição.</para>
/// </remarks>
/// <param name="vinculos">Papel do usuário na turma.</param>
/// <param name="acessos">Acessos da portaria.</param>
public sealed class PortariaHandler(IVinculoRepository vinculos, IAcessoDaPortariaRepository acessos) : AuthorizationHandler<PortariaRequirement>
{
    private static readonly string[] DaGestao = [PapelNaFormatura.Presidente, PapelNaFormatura.Tesoureiro, PapelNaFormatura.Comissao];

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PortariaRequirement requirement)
    {
        if (
            !Guid.TryParse(context.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            || !Guid.TryParse(context.User.FindFirstValue(TokenService.ClaimDeFormatura), out var formaturaId)
        )
            return;

        var ct = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;

        var passa = context.User.HasClaim(claim => claim.Type == TokenService.ClaimDeEvento)
            ? await acessos.AtivoDeTodasAsFormaturas(id, formaturaId, ct)
            : await vinculos.ObterPapelAtivo(id, formaturaId, ct) is { } papel && DaGestao.Contains(papel, StringComparer.Ordinal);

        if (passa)
            context.Succeed(requirement);
    }
}
