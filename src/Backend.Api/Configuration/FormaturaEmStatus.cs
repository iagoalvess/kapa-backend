using System.Security.Claims;
using Backend.Business.Auth.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Microsoft.AspNetCore.Authorization;

namespace Backend.Api.Configuration;

/// <summary>Exige que a formatura da sessão esteja num dos status aceitos.</summary>
/// <param name="aceitos">Status em que a escrita passa.</param>
public sealed class FormaturaEmStatusRequirement(params StatusDaFormatura[] aceitos) : IAuthorizationRequirement
{
    /// <summary>Status em que a escrita passa.</summary>
    public IReadOnlyList<StatusDaFormatura> Aceitos { get; } = aceitos;
}

/// <summary>
/// Confere o status gravado da formatura a cada escrita de domínio.
/// </summary>
/// <remarks>
/// Fora dos status aceitos a escrita recusa com <c>formatura.inativa</c> (ver
/// <see cref="RespostaDeAutorizacao"/>), e a leitura segue livre — suspender não é sequestrar dado.
/// <para>Scoped, porque depende do repositório da requisição.</para>
/// </remarks>
/// <param name="formaturaRepository">Status da formatura.</param>
public sealed class FormaturaEmStatusHandler(IFormaturaRepository formaturaRepository) : AuthorizationHandler<FormaturaEmStatusRequirement>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, FormaturaEmStatusRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(TokenService.ClaimDeFormatura), out var formaturaId))
            return;

        var ct = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;

        if (await formaturaRepository.ObterStatus(formaturaId, ct) is { } status && requirement.Aceitos.Contains(status))
            context.Succeed(requirement);
    }
}
