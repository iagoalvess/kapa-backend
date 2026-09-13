using System.Security.Claims;
using Backend.Business.Auth.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Backend.Api.Configuration;

/// <summary>
/// Exige que o usuário tenha, <b>agora</b>, um dos papéis informados na formatura da sessão.
/// </summary>
/// <param name="papeis">Papéis aceitos além do Presidente, que sempre passa.</param>
public sealed class PapelNaFormaturaRequirement(IReadOnlyList<string> papeis) : IAuthorizationRequirement
{
    /// <summary>Papéis aceitos além do Presidente.</summary>
    public IReadOnlyList<string> Papeis { get; } = papeis;
}

/// <summary>
/// Confere o papel no vínculo gravado, e não na claim do token.
/// </summary>
/// <remarks>
/// A claim <c>papel</c> é uma fotografia do momento da emissão e vale até quinze minutos. Ler só
/// ela deixaria o tesoureiro rebaixado — ou o membro removido — operando o caixa até o token
/// vencer. Aqui cada requisição protegida por papel consulta o vínculo ativo, pelo índice único
/// <c>(usuario_id, formatura_id)</c>; a claim continua existindo só para a tela decidir o que
/// mostrar.
/// <para>
/// <b>O Presidente é coringa dentro da formatura</b>, como o administrador é na plataforma.
/// </para>
/// <para>
/// Scoped, porque depende do repositório da requisição.
/// </para>
/// </remarks>
/// <param name="vinculoRepository">Vínculos entre usuário e formatura.</param>
public sealed class PapelNaFormaturaHandler(IVinculoRepository vinculoRepository) : AuthorizationHandler<PapelNaFormaturaRequirement>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PapelNaFormaturaRequirement requirement)
    {
        if (
            !Guid.TryParse(context.User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var usuarioId)
            || !Guid.TryParse(context.User.FindFirstValue(TokenService.ClaimDeFormatura), out var formaturaId)
        )
            return;

        var ct = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        var papel = await vinculoRepository.ObterPapelAtivo(usuarioId, formaturaId, ct);

        if (papel == PapelNaFormatura.Presidente || (papel is not null && requirement.Papeis.Contains(papel, StringComparer.Ordinal)))
            context.Succeed(requirement);
    }
}
