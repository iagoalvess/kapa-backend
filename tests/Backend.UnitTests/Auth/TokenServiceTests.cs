using Backend.Business.Auth.Services;
using Backend.Business.Auth.Settings;
using Backend.Business.Usuarios.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace Backend.UnitTests.Auth;

/// <summary>
/// Garante o formato do token e as propriedades de segurança do refresh token.
/// </summary>
public sealed class TokenServiceTests
{
    private static readonly JwtSettings Settings = new()
    {
        Emissor = "backend-testes",
        Audiencia = "clientes-testes",
        ChaveSecreta = "chave-de-teste-com-mais-de-32-caracteres-ok",
        MinutosDeValidadeDoAccessToken = 15,
        DiasDeValidadeDoRefreshToken = 7,
    };

    private static readonly Usuario Usuario = new()
    {
        Nome = "Ana Souza",
        Email = "ana@exemplo.com",
        UserName = "ana@exemplo.com",
    };

    private static TokenService Criar() => new(Options.Create(Settings));

    [Fact]
    public void Access_token_carrega_identidade_emissor_e_audiencia()
    {
        var gerado = Criar().GerarAccessToken(Usuario, [PerfisPadrao.Administrador]);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(gerado.Token);

        token.Issuer.ShouldBe(Settings.Emissor);
        token.Audiences.ShouldContain(Settings.Audiencia);
        token.GetClaim(JwtRegisteredClaimNames.Sub).Value.ShouldBe(Usuario.Id.ToString());
        token.GetClaim(JwtRegisteredClaimNames.Email).Value.ShouldBe(Usuario.Email);
    }

    [Fact]
    public void Access_token_carrega_um_claim_por_perfil()
    {
        var gerado = Criar().GerarAccessToken(Usuario, [PerfisPadrao.Administrador, PerfisPadrao.Usuario]);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(gerado.Token);
        var perfis = token.Claims.Where(claim => claim.Type == TokenService.ClaimDePerfil).Select(claim => claim.Value).ToList();

        perfis.ShouldBe([PerfisPadrao.Administrador, PerfisPadrao.Usuario], ignoreOrder: true);
    }

    [Fact]
    public void Access_token_expira_no_prazo_configurado()
    {
        var gerado = Criar().GerarAccessToken(Usuario, []);

        gerado.ExpiraEm.ShouldBeInRange(
            DateTime.UtcNow.AddMinutes(Settings.MinutosDeValidadeDoAccessToken - 1),
            DateTime.UtcNow.AddMinutes(Settings.MinutosDeValidadeDoAccessToken + 1)
        );
    }

    [Fact]
    public void Cada_refresh_token_e_diferente_do_anterior()
    {
        var servico = Criar();

        var tokens = Enumerable.Range(0, 50).Select(_ => servico.GerarRefreshToken().Token).ToHashSet(StringComparer.Ordinal);

        tokens.Count.ShouldBe(50);
    }

    [Fact]
    public void O_hash_persistido_nao_e_o_token_entregue()
    {
        var gerado = Criar().GerarRefreshToken();

        gerado.Hash.ShouldNotBe(gerado.Token);
        gerado.Hash.Length.ShouldBe(64);
    }

    [Fact]
    public void O_hash_do_mesmo_token_e_sempre_igual()
    {
        var servico = Criar();
        var gerado = servico.GerarRefreshToken();

        servico.CalcularHash(gerado.Token).ShouldBe(gerado.Hash);
    }
}
