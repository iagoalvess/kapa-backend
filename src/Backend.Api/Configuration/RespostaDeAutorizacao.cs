using Backend.Business.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Backend.Api.Configuration;

/// <summary>
/// Separa "você não escolheu turma" de "você não pode".
/// </summary>
/// <remarks>
/// São dois 403 com remédios opostos: o primeiro se resolve mandando o usuário para a seleção de
/// formatura, o segundo não se resolve de jeito nenhum. Com um código só, o front não teria como
/// saber qual dos dois aconteceu — e a escolha razoável, redirecionar, viraria um laço para quem
/// simplesmente não tem permissão.
/// <para>
/// A decisão olha o <b>requisito que falhou</b>, e não se o token tem a claim: um usuário sem
/// formatura barrado numa política de administrador não tem o que escolher — mandá-lo para a
/// seleção só trocaria um 403 por outro. O resto segue o fluxo padrão, que responde
/// <c>auth.sem_permissao</c> pelo <c>OnForbidden</c> do JWT.
/// </para>
/// </remarks>
public sealed class RespostaDeAutorizacao : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _padrao = new();

    /// <inheritdoc />
    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && FaltouFormatura(authorizeResult.AuthorizationFailure))
            return AuthConfig.EscreverProblema(
                context,
                StatusCodes.Status403Forbidden,
                "formatura.nao_selecionada",
                "Nenhuma formatura selecionada."
            );

        if (authorizeResult.Forbidden && SoFaltouFormaturaAtiva(authorizeResult.AuthorizationFailure))
            return AuthConfig.EscreverProblema(
                context,
                StatusCodes.Status403Forbidden,
                "formatura.inativa",
                "Esta formatura não está ativa. A turma está em modo leitura."
            );

        return _padrao.HandleAsync(next, context, policy, authorizeResult);
    }

    /// <summary>
    /// A única coisa que faltou foi a formatura estar ativa.
    /// </summary>
    /// <remarks>
    /// Se o papel também faltou, a resposta é <c>auth.sem_permissao</c>: o formando barrado numa
    /// escrita de tesouraria não ganharia nada sabendo que a turma está suspensa — mesmo ativa ele
    /// não passaria.
    /// </remarks>
    /// <param name="falha">Falha de autorização.</param>
    private static bool SoFaltouFormaturaAtiva(AuthorizationFailure? falha) =>
        falha?.FailedRequirements.Any() == true && falha.FailedRequirements.All(requisito => requisito is FormaturaEmStatusRequirement);

    private static bool FaltouFormatura(AuthorizationFailure? falha) =>
        falha?.FailedRequirements.OfType<ClaimsAuthorizationRequirement>().Any(requisito => requisito.ClaimType == TokenService.ClaimDeFormatura)
        == true;
}
