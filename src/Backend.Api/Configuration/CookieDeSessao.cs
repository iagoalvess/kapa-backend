using Backend.Business.Auth.Settings;
using Microsoft.Extensions.Options;

namespace Backend.Api.Configuration;

/// <summary>
/// Como o refresh token viaja até o navegador.
/// </summary>
/// <remarks>
/// Ligado por padrão. Com o cookie <c>HttpOnly</c>, o refresh token deixa de estar ao alcance do
/// JavaScript: um XSS não consegue <b>levar</b> a credencial embora e reusá-la de outra máquina
/// pelos dias de validade dela.
/// <para>
/// <b>Isto não cura XSS.</b> O cookie viaja sozinho, então o atacante continua conseguindo agir
/// como o usuário enquanto a página estiver aberta. A troca é "sessão roubada por dias, de
/// qualquer lugar" por "abuso enquanto a aba está aberta" — melhora real, não cura.
/// </para>
/// <para>
/// Desligue (<c>false</c>) quando o cliente não for navegador — aplicativo móvel, integração
/// servidor a servidor — e o refresh token voltar a ser devolvido no corpo da resposta.
/// </para>
/// </remarks>
public sealed class CookieDeSessaoSettings
{
    /// <summary>Nome da seção correspondente no arquivo de configuração.</summary>
    public const string Secao = "CookieDeSessao";

    /// <summary>Se o refresh token viaja em cookie em vez do corpo da resposta.</summary>
    public bool Habilitado { get; init; } = true;

    /// <summary>Nome do cookie.</summary>
    public string Nome { get; init; } = "refresh_token";

    /// <summary>
    /// Política <c>SameSite</c>.
    /// </summary>
    /// <remarks>
    /// <c>Lax</c> atende o caso normal — front e API sob o mesmo site registrável
    /// (<c>app.exemplo.com</c> e <c>api.exemplo.com</c>) — e sozinho já barra CSRF.
    /// <para>
    /// Só use <c>None</c> quando front e API ficarem em sites registráveis diferentes
    /// (<c>algo.vercel.app</c> e <c>algo.fly.dev</c>). <b>Aí o SameSite deixa de proteger contra
    /// CSRF</b>, e a defesa passa a ser a checagem de origem que o
    /// <see cref="CookieDeSessao.LerRefreshToken"/> faz.
    /// </para>
    /// </remarks>
    public SameSiteMode SameSite { get; init; } = SameSiteMode.Lax;
}

/// <summary>
/// Escreve, lê e apaga o cookie que carrega o refresh token.
/// </summary>
/// <remarks>
/// Fica na Api, e não em Business: cookie é transporte. A camada de negócio continua recebendo e
/// devolvendo o token como texto, sem saber por onde ele trafegou.
/// </remarks>
public static class CookieDeSessao
{
    /// <summary>
    /// Caminho do cookie.
    /// </summary>
    /// <remarks>
    /// <c>/api</c>, e não <c>/api/v1/auth</c>: amarrar o caminho à versão faria o cookie sumir no
    /// dia em que a v2 nascer, e o sintoma seria "todo mundo deslogado" sem nada no log. O custo
    /// de mandá-lo nas demais chamadas é uma centena de bytes; ele continua <c>HttpOnly</c> e
    /// protegido por <c>SameSite</c>.
    /// </remarks>
    private const string Caminho = "/api";

    /// <summary>Grava o refresh token no cookie.</summary>
    /// <param name="resposta">Resposta em construção.</param>
    /// <param name="refreshToken">Token a guardar.</param>
    /// <param name="cookie">Configuração do cookie.</param>
    /// <param name="jwt">Configuração de JWT, que define por quanto tempo o cookie vale.</param>
    public static void Gravar(this HttpResponse resposta, string refreshToken, CookieDeSessaoSettings cookie, JwtSettings jwt)
    {
        resposta.Cookies.Append(cookie.Nome, refreshToken, Opcoes(cookie, DateTimeOffset.UtcNow.AddDays(jwt.DiasDeValidadeDoRefreshToken)));
    }

    /// <summary>Apaga o cookie.</summary>
    /// <remarks>
    /// Os atributos precisam ser os mesmos da gravação — caminho e <c>SameSite</c> incluídos.
    /// Divergindo em qualquer um deles, o navegador entende como outro cookie e o original
    /// sobrevive ao logout.
    /// </remarks>
    /// <param name="resposta">Resposta em construção.</param>
    /// <param name="cookie">Configuração do cookie.</param>
    public static void Apagar(this HttpResponse resposta, CookieDeSessaoSettings cookie)
    {
        resposta.Cookies.Delete(cookie.Nome, Opcoes(cookie, expiraEm: null));
    }

    /// <summary>
    /// Lê o refresh token do cookie, recusando pedido de origem não declarada.
    /// </summary>
    /// <remarks>
    /// A checagem de <c>Origin</c> é a defesa de CSRF que sobra quando <c>SameSite</c> é
    /// <c>None</c>: o navegador envia o cabeçalho em toda requisição de origem cruzada e não
    /// deixa a página forjá-lo. Origem ausente é requisição de mesma origem ou de cliente que não
    /// é navegador — os dois casos passam.
    /// </remarks>
    /// <param name="requisicao">Requisição recebida.</param>
    /// <param name="cookie">Configuração do cookie.</param>
    /// <param name="origensPermitidas">Origens declaradas em <c>Cors:Origens</c>.</param>
    /// <returns>O token, ou nulo se não houver cookie ou a origem não for permitida.</returns>
    public static string? LerRefreshToken(this HttpRequest requisicao, CookieDeSessaoSettings cookie, IReadOnlyList<string> origensPermitidas)
    {
        if (!requisicao.Cookies.TryGetValue(cookie.Nome, out var token) || string.IsNullOrWhiteSpace(token))
            return null;

        var origem = requisicao.Headers.Origin.ToString();

        if (!string.IsNullOrEmpty(origem) && !origensPermitidas.Contains(origem, StringComparer.OrdinalIgnoreCase))
            return null;

        return token;
    }

    /// <summary>
    /// Monta os atributos do cookie.
    /// </summary>
    /// <remarks>
    /// <c>Secure</c> é incondicional, inclusive em desenvolvimento: navegador trata
    /// <c>http://localhost</c> como contexto seguro e aceita cookie <c>Secure</c> ali, então não
    /// há nada a ganhar deixando-o condicional — e um condicional errado significa refresh token
    /// trafegando em claro, que é exatamente o que este cookie existe para evitar.
    /// <para>
    /// Quem consome fora do navegador — <c>HttpClient</c>, <c>curl</c> — precisa falar
    /// <c>https</c> para receber o cookie. É o caso dos testes de integração.
    /// </para>
    /// </remarks>
    private static CookieOptions Opcoes(CookieDeSessaoSettings cookie, DateTimeOffset? expiraEm) =>
        new()
        {
            HttpOnly = true,
            Secure = true,
            SameSite = cookie.SameSite,
            Path = Caminho,
            Expires = expiraEm,
            IsEssential = true,
        };
}

/// <summary>Registro da configuração do cookie de sessão.</summary>
public static class CookieDeSessaoConfig
{
    /// <summary>Liga a seção de configuração do cookie.</summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="configuration">Configuração da aplicação.</param>
    public static IServiceCollection AddCookieDeSessao(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CookieDeSessaoSettings>(configuration.GetSection(CookieDeSessaoSettings.Secao));

        return services;
    }
}
