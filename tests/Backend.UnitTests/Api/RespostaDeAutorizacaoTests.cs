using Backend.Api.Configuration;
using Backend.Business.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace Backend.UnitTests.Api;

/// <summary>
/// O 403 de quem ainda não escolheu turma tem código próprio: o front resolve um redirecionando
/// para a seleção, e o outro não se resolve de jeito nenhum.
/// </summary>
/// <remarks>
/// Unitário porque nenhum endpoint usa <c>FormaturaSelecionada</c> ainda. O outro lado — sem
/// formatura, barrado por política de administrador, responde <c>auth.sem_permissao</c> — está
/// nos testes de integração, onde o <c>OnForbidden</c> do JWT roda de verdade.
/// </remarks>
public sealed class RespostaDeAutorizacaoTests
{
    [Fact]
    public async Task Falhar_no_requisito_de_formatura_responde_nao_selecionada()
    {
        // Arrange
        var contexto = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var requisito = new ClaimsAuthorizationRequirement(TokenService.ClaimDeFormatura, allowedValues: null);
        var politica = new AuthorizationPolicyBuilder().AddRequirements(requisito).Build();
        var resultado = PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed([requisito]));

        // Act
        await new RespostaDeAutorizacao().HandleAsync(_ => Task.CompletedTask, contexto, politica, resultado);

        // Assert
        contexto.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);

        contexto.Response.Body.Position = 0;
        var corpo = await new StreamReader(contexto.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        corpo.ShouldContain("formatura.nao_selecionada");
    }
}
