using System.Security.Claims;
using Backend.Business.Auth.Services;
using Backend.Business.Formaturas.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Backend.Api.Configuration;

/// <summary>
/// O formando só usa o app depois de aderir ao termo da turma (Sprint 47, D18).
/// </summary>
/// <remarks>
/// Vai junto do papel em <see cref="Politicas.MembroDaFormatura"/>, o piso de todo endpoint de domínio: a guarda do front
/// é conforto, quem barra o atalho pela URL é isto. A Gestão não é barrada — ela monta a turma antes de qualquer adesão.
/// </remarks>
public sealed class AdesaoNaFormaturaRequirement : IAuthorizationRequirement;

/// <summary>
/// Recusa o formando que ainda não aderiu ao termo publicado da turma.
/// </summary>
/// <remarks>
/// Barra só quando há termo publicado: a Sprint 47 (D34) proíbe convidar formando antes do termo e do catálogo, então
/// formando sem termo a que aderir só existe em turma anterior a essa regra — e prendê-lo numa tela sem saída seria
/// pior que deixá-lo entrar. Uma consulta a mais por requisição do formando, pelo índice do vínculo.
/// </remarks>
/// <param name="vinculoRepository">Vínculos e adesões.</param>
public sealed class AdesaoNaFormaturaHandler(IVinculoRepository vinculoRepository) : AuthorizationHandler<AdesaoNaFormaturaRequirement>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdesaoNaFormaturaRequirement requirement)
    {
        if (
            !Guid.TryParse(context.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var usuarioId)
            || !Guid.TryParse(context.User.FindFirstValue(TokenService.ClaimDeFormatura), out var formaturaId)
        )
            return;

        var ct = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;

        if (!await vinculoRepository.FormandoSemAdesao(usuarioId, formaturaId, ct))
            context.Succeed(requirement);
    }
}
