using System.Security.Claims;
using Backend.Api.Configuration;
using Backend.Business.Auth.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Api;

/// <summary>
/// A matriz de permissões da Sprint 1, conferida papel por papel contra as políticas.
/// </summary>
/// <remarks>
/// <see cref="Matriz"/> é a tabela do documento da sprint transcrita: cada linha é um módulo, a
/// política que o protege e os papéis que passam. Mudou a tabela, muda aqui — e o teste diz se o
/// código concorda. É o que impede a matriz de divergir do código com o tempo.
/// <para>
/// O papel vem do <b>vínculo gravado</b> (repositório substituído), não da claim do token: é
/// assim que a política decide em produção.
/// </para>
/// </remarks>
public sealed class MatrizDePermissoesTests
{
    private const string P = PapelNaFormatura.Presidente;
    private const string T = PapelNaFormatura.Tesoureiro;
    private const string C = PapelNaFormatura.Comissao;
    private const string F = PapelNaFormatura.Formando;

    /// <summary>Módulo, política e papéis que têm acesso.</summary>
    public static TheoryData<string, string, string[]> Matriz =>
        new()
        {
            { "Dados da formatura (editar)", Politicas.SomentePresidente, [P] },
            { "Assinatura e plano SaaS", Politicas.SomentePresidente, [P] },
            { "Convidar / remover membros", Politicas.SomentePresidente, [P] },
            { "Alterar papel de membro", Politicas.SomentePresidente, [P] },
            { "Configurar cobranças", Politicas.Tesouraria, [P, T] },
            { "Lançar despesa / fornecedor", Politicas.Tesouraria, [P, T] },
            { "Baixar parcela manualmente", Politicas.Tesouraria, [P, T] },
            { "Renegociar dívida", Politicas.Tesouraria, [P, T] },
            { "Ver extrato de qualquer formando", Politicas.Gestao, [P, T, C] },
            { "Publicar aviso / documento", Politicas.Gestao, [P, T, C] },
            { "Dashboard financeiro com detalhe por pessoa", Politicas.Gestao, [P, T, C] },
            { "Dashboard público da turma", Politicas.MembroDaFormatura, [P, T, C, F] },
            { "Próprio extrato e próprio cadastro", Politicas.MembroDaFormatura, [P, T, C, F] },
        };

    /// <summary>As políticas de papel, para os casos que valem para todas.</summary>
    public static TheoryData<string> PoliticasDePapel =>
        [Politicas.SomentePresidente, Politicas.Tesouraria, Politicas.Gestao, Politicas.MembroDaFormatura];

    [Theory]
    [MemberData(nameof(Matriz))]
    public async Task Cada_papel_passa_so_onde_a_matriz_permite(string modulo, string politica, string[] permitidos)
    {
        foreach (var papel in PapelNaFormatura.Todos)
        {
            var resultado = await ServicoComPapelGravado(papel).AuthorizeAsync(NaFormatura(papel), resource: null, politica);

            resultado.Succeeded.ShouldBe(permitidos.Contains(papel), $"{modulo} para {papel}");
        }
    }

    /// <summary>Papel sem formatura não significa nada — nem o do Presidente.</summary>
    [Theory]
    [MemberData(nameof(PoliticasDePapel))]
    public async Task Sem_formatura_selecionada_nenhum_papel_passa(string politica)
    {
        var semFormatura = Autenticado(
            new Claim(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString()),
            new Claim(TokenService.ClaimDePapel, P)
        );

        var resultado = await ServicoComPapelGravado(P).AuthorizeAsync(semFormatura, resource: null, politica);

        resultado.Succeeded.ShouldBeFalse();
    }

    /// <summary>
    /// O token diz Presidente, o vínculo diz Formando: vale o vínculo. É o caso de quem foi
    /// rebaixado com um access token ainda válido.
    /// </summary>
    [Theory]
    [MemberData(nameof(PoliticasDePapel))]
    public async Task Vale_o_papel_gravado_e_nao_o_da_claim(string politica)
    {
        var resultado = await ServicoComPapelGravado(F).AuthorizeAsync(NaFormatura(P), resource: null, politica);

        resultado.Succeeded.ShouldBe(politica == Politicas.MembroDaFormatura);
    }

    /// <summary>Vínculo desativado corta o acesso na requisição seguinte, mesmo com o token válido.</summary>
    [Theory]
    [MemberData(nameof(PoliticasDePapel))]
    public async Task Membro_removido_nao_passa_em_nenhuma_politica_de_papel(string politica)
    {
        var resultado = await ServicoComPapelGravado(null).AuthorizeAsync(NaFormatura(P), resource: null, politica);

        resultado.Succeeded.ShouldBeFalse();
    }

    private static ClaimsPrincipal NaFormatura(string papelNaClaim) =>
        Autenticado(
            new Claim(JwtRegisteredClaimNames.Sub, Guid.CreateVersion7().ToString()),
            new Claim(TokenService.ClaimDeFormatura, Guid.CreateVersion7().ToString()),
            new Claim(TokenService.ClaimDePapel, papelNaClaim)
        );

    private static ClaimsPrincipal Autenticado(params Claim[] claims) => new(new ClaimsIdentity(claims, "Bearer"));

    /// <summary>Serviço de autorização cujo repositório responde o papel informado para qualquer vínculo.</summary>
    /// <param name="papelGravado">Papel no vínculo ativo, ou nulo para "sem vínculo ativo".</param>
    private static IAuthorizationService ServicoComPapelGravado(string? papelGravado)
    {
        var vinculos = Substitute.For<IVinculoRepository>();
        vinculos.ObterPapelAtivo(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(papelGravado);

        var services = new ServiceCollection();

        services.AddLogging(opcoes => opcoes.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(vinculos);
        services.AddSingleton(Substitute.For<IFormaturaRepository>());
        services.AddPoliticas();

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IAuthorizationService>();
    }
}
