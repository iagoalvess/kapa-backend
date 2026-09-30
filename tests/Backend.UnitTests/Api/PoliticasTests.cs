using System.Security.Claims;
using Backend.Api.Configuration;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Auth.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
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
    /// <summary>Serviço de autorização cuja formatura, qualquer que seja, está no status informado.</summary>
    /// <param name="status">Status gravado, ou nulo para formatura inexistente.</param>
    /// <param name="politicaDeUsuario">Registra, com este nome, uma política que exige o perfil <c>Usuario</c>.</param>
    private static IAuthorizationService ServicoComStatus(StatusDaFormatura? status, string? politicaDeUsuario = null)
    {
        var formaturas = Substitute.For<IFormaturaRepository>();
        formaturas.ObterStatus(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(status);

        var services = new ServiceCollection();

        services.AddLogging(opcoes => opcoes.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(Substitute.For<IVinculoRepository>());
        services.AddSingleton(formaturas);
        services.AddSingleton(Substitute.For<IAssinaturaRepository>());
        services.AddPoliticas();

        if (politicaDeUsuario is not null)
            services.AddAuthorizationBuilder().AddPolicy(politicaDeUsuario, politica => politica.RequireAuthenticatedUser().ExigirPerfil("Usuario"));

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IAuthorizationService>();
    }

    /// <summary>Só <c>Ativa</c> escreve; suspensa, encerrada e descartada são leitura.</summary>
    [Theory]
    [InlineData(StatusDaFormatura.Descartada, false)]
    [InlineData(StatusDaFormatura.Ativa, true)]
    [InlineData(StatusDaFormatura.Suspensa, false)]
    [InlineData(StatusDaFormatura.Encerrada, false)]
    [InlineData(null, false)]
    public async Task Exige_formatura_ativa_so_passa_com_status_ativa(StatusDaFormatura? status, bool passa)
    {
        var usuario = Autenticado(new Claim(TokenService.ClaimDeFormatura, Guid.CreateVersion7().ToString()));

        var resultado = await ServicoComStatus(status).AuthorizeAsync(usuario, resource: null, Politicas.ExigeFormaturaAtiva);

        resultado.Succeeded.ShouldBe(passa);
    }

    [Fact]
    public async Task Exige_formatura_ativa_sem_a_claim_recusa()
    {
        var usuario = Autenticado(new Claim(TokenService.ClaimDePerfil, "Usuario"));

        var resultado = await ServicoComStatus(StatusDaFormatura.Ativa).AuthorizeAsync(usuario, resource: null, Politicas.ExigeFormaturaAtiva);

        resultado.Succeeded.ShouldBeFalse();
    }

    private static ClaimsPrincipal Autenticado(params Claim[] claims) => new(new ClaimsIdentity(claims, "Bearer"));

    /// <summary>
    /// Nem o administrador entra sem escolher turma: não é falta de permissão, é falta de
    /// contexto — e o dado que ele veria seria o de nenhuma formatura.
    /// </summary>
    [Fact]
    public async Task Nem_o_administrador_passa_sem_formatura_selecionada()
    {
        var usuario = Autenticado(new Claim(TokenService.ClaimDePerfil, "Administrador"));

        var resultado = await ServicoComStatus(StatusDaFormatura.Ativa).AuthorizeAsync(usuario, resource: null, Politicas.ExigeFormaturaAtiva);

        resultado.Succeeded.ShouldBeFalse();
    }

    /// <summary>
    /// D4 da Sprint 44: o administrador não é coringa. Política de perfil aprova só quem consta dela — o painel,
    /// que o lista, continua aberto a ele.
    /// </summary>
    [Theory]
    [InlineData("Administrador", Politicas.SomenteAdministrador, true)]
    [InlineData("Usuario", Politicas.SomenteAdministrador, false)]
    [InlineData("Administrador", PoliticaDeUsuario, false)]
    [InlineData("Usuario", PoliticaDeUsuario, true)]
    public async Task Politica_de_perfil_aprova_so_os_perfis_listados(string perfil, string politica, bool passa)
    {
        var usuario = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, perfil)], "Bearer"));

        var resultado = await ServicoComStatus(StatusDaFormatura.Ativa, PoliticaDeUsuario).AuthorizeAsync(usuario, resource: null, politica);

        resultado.Succeeded.ShouldBe(passa);
    }

    /// <summary>Uma política de perfil qualquer, só para o teste, com o perfil de conta comum.</summary>
    private const string PoliticaDeUsuario = nameof(PoliticaDeUsuario);
}
