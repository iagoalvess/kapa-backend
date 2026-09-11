using System.Security.Claims;
using Backend.Api.Configuration;
using Backend.Business.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Backend.UnitTests.Api;

/// <summary>
/// Garante que a política de formatura selecionada exige a claim, e não a boa vontade de quem
/// escreveu o endpoint.
/// </summary>
/// <remarks>
/// Política mal declarada não quebra o build: o endpoint simplesmente passa a aceitar quem não
/// deveria, e ninguém descobre até alguém ver dado de outra turma.
/// </remarks>
public sealed class PoliticasTests
{
    private static IAuthorizationService Servico()
    {
        var services = new ServiceCollection();

        services.AddLogging(opcoes => opcoes.SetMinimumLevel(LogLevel.None));
        services.AddPoliticas();

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Autenticado(params Claim[] claims) => new(new ClaimsIdentity(claims, "Bearer"));

    [Fact]
    public async Task Sem_a_claim_de_formatura_a_politica_recusa()
    {
        var usuario = Autenticado(new Claim(TokenService.ClaimDePerfil, "Usuario"));

        var resultado = await Servico().AuthorizeAsync(usuario, resource: null, Politicas.FormaturaSelecionada);

        resultado.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Com_a_claim_de_formatura_a_politica_aceita()
    {
        var usuario = Autenticado(new Claim(TokenService.ClaimDeFormatura, Guid.CreateVersion7().ToString()));

        var resultado = await Servico().AuthorizeAsync(usuario, resource: null, Politicas.FormaturaSelecionada);

        resultado.Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// Nem o administrador entra sem escolher turma: não é falta de permissão, é falta de
    /// contexto — e o dado que ele veria seria o de nenhuma formatura.
    /// </summary>
    [Fact]
    public async Task Nem_o_administrador_passa_sem_formatura_selecionada()
    {
        var usuario = Autenticado(new Claim(TokenService.ClaimDePerfil, "Administrador"));

        var resultado = await Servico().AuthorizeAsync(usuario, resource: null, Politicas.FormaturaSelecionada);

        resultado.Succeeded.ShouldBeFalse();
    }
}
