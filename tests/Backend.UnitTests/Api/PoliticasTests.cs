using System.Security.Claims;
using Backend.Api.Configuration;
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
    private static IAuthorizationService Servico()
    {
        var services = new ServiceCollection();

        services.AddLogging(opcoes => opcoes.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(Substitute.For<IVinculoRepository>());
        services.AddSingleton(Substitute.For<IFormaturaRepository>());
        services.AddPoliticas();

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    /// <summary>Serviço de autorização cuja formatura, qualquer que seja, está no status informado.</summary>
    /// <param name="status">Status gravado, ou nulo para formatura inexistente.</param>
    private static IAuthorizationService ServicoComStatus(StatusDaFormatura? status)
    {
        var formaturas = Substitute.For<IFormaturaRepository>();
        formaturas.ObterStatus(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(status);

        var services = new ServiceCollection();

        services.AddLogging(opcoes => opcoes.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(Substitute.For<IVinculoRepository>());
        services.AddSingleton(formaturas);
        services.AddPoliticas();

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IAuthorizationService>();
    }

    /// <summary>
    /// Só <c>Ativa</c> escreve. Rascunho e aguardando pagamento ainda não contrataram; suspensa e
    /// encerrada são leitura.
    /// </summary>
    [Theory]
    [InlineData(StatusDaFormatura.Rascunho, false)]
    [InlineData(StatusDaFormatura.AguardandoPagamento, false)]
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
