using Backend.Api.DTOs.Auth;
using Backend.Business.Abstractions;
using Backend.Business.Auth.Models;
using Backend.Business.Auth.Settings;
using Microsoft.Extensions.Options;

namespace Backend.Api.Configuration;

/// <summary>
/// As origens declaradas em <c>Cors:Origens</c>, lidas uma vez na vida do processo.
/// </summary>
/// <param name="Valores">Origens aceitas na leitura do cookie de sessão.</param>
public sealed record OrigensPermitidas(IReadOnlyList<string> Valores);

/// <summary>
/// O transporte da sessão na requisição atual: de onde vem o refresh token e como o par volta.
/// </summary>
/// <remarks>
/// Existe para que todo endpoint que lê ou emite sessão — login, registro, renovação, aceite de
/// convite, criação e seleção de formatura — faça isso do mesmo jeito. Antes, cada controller
/// recebia as opções do cookie, as de JWT e a configuração inteira para achar as origens, e o
/// dia em que um deles divergisse seria o dia em que o refresh token voltaria para o corpo.
/// <para>
/// Por baixo, continua sendo <see cref="CookieDeSessao"/> e <see cref="RespostaDeSessao"/>: esta
/// classe só junta a requisição e as configurações que eles pedem.
/// </para>
/// </remarks>
/// <param name="acessor">A requisição em andamento.</param>
/// <param name="cookieOptions">Configuração do cookie de sessão.</param>
/// <param name="jwtOptions">Configuração de JWT, que define a validade do cookie.</param>
/// <param name="origens">Origens aceitas na leitura do cookie.</param>
public sealed class SessaoHttp(
    IHttpContextAccessor acessor,
    IOptions<CookieDeSessaoSettings> cookieOptions,
    IOptions<JwtSettings> jwtOptions,
    OrigensPermitidas origens
)
{
    private HttpContext Contexto => acessor.HttpContext ?? throw new InvalidOperationException("Sessão HTTP usada fora de uma requisição.");

    /// <summary>
    /// O refresh token da requisição: do cookie no modo cookie, do corpo com ele desligado.
    /// </summary>
    /// <param name="doCorpo">Token enviado no corpo, usado só com o modo cookie desligado.</param>
    /// <returns>O token, ou vazio se não houver.</returns>
    public string RefreshTokenRecebido(string? doCorpo) => Contexto.Request.RefreshTokenRecebido(cookieOptions.Value, origens.Valores, doCorpo);

    /// <summary>
    /// Prepara o corpo da resposta, mandando o refresh token pelo cookie quando o modo está ligado.
    /// </summary>
    /// <param name="resultado">Resultado devolvido pelo service.</param>
    /// <returns>O resultado já no formato do contrato público, ou a falha intacta.</returns>
    public Result<TokenResponseDTO> Preparar(Result<ParDeTokens> resultado) =>
        RespostaDeSessao.Preparar(resultado, Contexto.Response, cookieOptions.Value, jwtOptions.Value);

    /// <summary>Apaga o cookie de sessão, quando o modo cookie está ligado; sem ele, não faz nada.</summary>
    public void ApagarCookie()
    {
        if (cookieOptions.Value.Habilitado)
            Contexto.Response.Apagar(cookieOptions.Value);
    }
}
