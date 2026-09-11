using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Auth.Settings;
using Backend.Business.Usuarios.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Business.Auth.Services;

/// <summary>
/// Emite access tokens JWT e refresh tokens opacos.
/// </summary>
/// <remarks>
/// Registrado como singleton: a chave de assinatura é derivada uma vez no construtor em vez de
/// a cada login. <see cref="JsonWebTokenHandler"/> é seguro para uso concorrente.
/// </remarks>
public sealed class TokenService : ITokenService
{
    /// <summary>Nome da claim que carrega os perfis. Precisa bater com o <c>RoleClaimType</c> configurado na API.</summary>
    public const string ClaimDePerfil = "role";

    /// <summary>Nome da claim que carrega a formatura selecionada na sessão.</summary>
    public const string ClaimDeFormatura = "formatura_id";

    /// <summary>Nome da claim que carrega o papel do usuário na formatura selecionada.</summary>
    public const string ClaimDePapel = "papel";

    private readonly JwtSettings _settings;
    private readonly SigningCredentials _credenciaisDeAssinatura;
    private readonly JsonWebTokenHandler _handler = new();

    /// <summary>Inicializa o serviço e prepara a chave de assinatura.</summary>
    /// <param name="options">Configuração de JWT, validada na subida.</param>
    public TokenService(IOptions<JwtSettings> options)
    {
        _settings = options.Value;

        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.ChaveSecreta));
        _credenciaisDeAssinatura = new SigningCredentials(chave, SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Token <b>sem</b> <c>formatura_id</c> continua válido: é o que serve para o login, a
    /// listagem de formaturas, o cadastro e o aceite de convite. Endpoint de domínio exige a
    /// claim pela política <c>FormaturaSelecionada</c>.
    /// </remarks>
    public AccessTokenGerado GerarAccessToken(Usuario usuario, IReadOnlyList<string> perfis, Guid? formaturaId = null, string? papel = null)
    {
        var agora = DateTime.UtcNow;
        var expiraEm = agora.AddMinutes(_settings.MinutosDeValidadeDoAccessToken);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Name, usuario.Nome),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
        };

        claims.AddRange(perfis.Select(perfil => new Claim(ClaimDePerfil, perfil)));

        if (formaturaId is not null)
            claims.Add(new Claim(ClaimDeFormatura, formaturaId.Value.ToString()));

        if (!string.IsNullOrWhiteSpace(papel))
            claims.Add(new Claim(ClaimDePapel, papel));

        var descritor = new SecurityTokenDescriptor
        {
            Issuer = _settings.Emissor,
            Audience = _settings.Audiencia,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = agora,
            NotBefore = agora,
            Expires = expiraEm,
            SigningCredentials = _credenciaisDeAssinatura,
        };

        return new AccessTokenGerado(_handler.CreateToken(descritor), expiraEm);
    }

    /// <inheritdoc />
    /// <remarks>
    /// O refresh token é opaco — 64 bytes de aleatoriedade criptográfica, sem estrutura e sem
    /// conteúdo. Não é um JWT de propósito: JWT carrega informação e é validado por assinatura,
    /// enquanto refresh token precisa ser **revogável**, o que exige consulta ao banco de
    /// qualquer forma. Se vai bater no banco, não há motivo para carregar dado dentro dele.
    /// </remarks>
    public RefreshTokenGerado GerarRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));

        return new RefreshTokenGerado(token, CalcularHash(token), DateTime.UtcNow.AddDays(_settings.DiasDeValidadeDoRefreshToken));
    }

    /// <inheritdoc />
    /// <remarks>
    /// SHA-256 puro, sem salt nem alongamento — ao contrário de senha. O token tem 512 bits de
    /// entropia gerados por CSPRNG, então não há o que atacar por dicionário ou força bruta;
    /// o hash existe só para que um vazamento do banco não entregue sessões utilizáveis.
    /// </remarks>
    public string CalcularHash(string tokenEmTextoPuro) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenEmTextoPuro)));
}
