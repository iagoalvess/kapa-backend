using Backend.Business.Abstractions;
using Backend.Business.Auth.Services;
using Backend.Business.Auth.Settings;
using Microsoft.Extensions.Options;

namespace Backend.Api.Configuration;

/// <summary>Cookies HttpOnly e proteção de origem do callback JavaScript GIS.</summary>
/// <remarks>O GIS não envia o double-submit do modo POST direto no callback JavaScript.</remarks>
public sealed class GoogleHttp(
    IHttpContextAccessor acessor,
    OrigensPermitidas origens,
    IOptions<GoogleSettings> options,
    IOptions<CookieDeSessaoSettings> cookies
)
{
    private const string Navegador = "kapa_google_navegador";
    private const string Contexto = "kapa_google_contexto";
    private HttpContext Http => acessor.HttpContext!;

    /// <summary>Confere origem explicitamente; inicia o cookie apenas numa tentativa solicitada pelo app.</summary>
    public Result<string> Conferir(bool iniciar = false)
    {
        Http.Response.Headers.CacheControl = "no-store";
        if (!options.Value.Habilitado)
            return Erro.NaoEncontrado("auth.google_desligado", "A entrada com Google não está disponível.");
        var origem = Http.Request.Headers.Origin.ToString();
        var propria = $"{Http.Request.Scheme}://{Http.Request.Host}";
        if (string.IsNullOrEmpty(origem) || (!origens.Valores.Contains(origem, StringComparer.OrdinalIgnoreCase) && origem != propria))
            return Erro.Proibido("auth.google_origem_invalida", "Inicie a entrada pelo aplicativo Kapa.");
        var navegador = Http.Request.Cookies[Navegador];
        if (iniciar)
        {
            if (string.IsNullOrEmpty(navegador))
                navegador = GoogleService.NovoToken();
            Http.Response.Cookies.Append(Navegador, navegador, Opcoes());
        }
        return string.IsNullOrEmpty(navegador)
            ? Erro.NaoAutenticado("auth.google_contexto_invalido", "Entre com Google novamente neste navegador.")
            : Result.Ok(navegador);
    }

    /// <summary>Guarda contexto de vínculo para o link abrir outra aba do mesmo navegador.</summary>
    public void GuardarContexto(string contexto)
    {
        Http.Response.Cookies.Append(Contexto, contexto, Opcoes());
        if (Http.Request.Cookies[Navegador] is { } navegador)
            Http.Response.Cookies.Append(Navegador, navegador, Opcoes());
    }

    /// <summary>Contexto do corpo ou do cookie HttpOnly, nunca da URL.</summary>
    public string LerContexto(string? corpo) => string.IsNullOrWhiteSpace(corpo) ? Http.Request.Cookies[Contexto] ?? string.Empty : corpo;

    /// <summary>Apaga o contexto depois do uso.</summary>
    public void LimparContexto() => Http.Response.Cookies.Delete(Contexto, Opcoes());

    private CookieOptions Opcoes() =>
        new()
        {
            HttpOnly = true,
            Secure = Http.Request.IsHttps,
            SameSite = cookies.Value.SameSite,
            Path = "/api/v1/auth/google",
            MaxAge = TimeSpan.FromMinutes(20),
            IsEssential = true,
        };
}
