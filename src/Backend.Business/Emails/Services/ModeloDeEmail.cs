using System.Net;

namespace Backend.Business.Emails.Services;

/// <summary>
/// O esqueleto HTML comum a todo e-mail da aplicação: título, mensagem, botão opcional e rodapé.
/// </summary>
/// <remarks>
/// HTML montado em código, sem motor de template — ver <c>EmailsDeConta</c>. Tudo o que vem do
/// usuário passa por <see cref="Texto"/> antes de entrar no corpo: nome de turma e de pessoa é
/// texto controlado por terceiro, e e-mail é HTML.
/// </remarks>
public static class ModeloDeEmail
{
    /// <summary>Codifica texto para entrar no HTML.</summary>
    /// <param name="valor">Texto cru.</param>
    public static string Texto(string? valor) => WebUtility.HtmlEncode(valor ?? string.Empty);

    /// <summary>Monta o corpo do e-mail.</summary>
    /// <param name="nomeDaAplicacao">Nome exibido no rodapé.</param>
    /// <param name="titulo">Título, codificado aqui.</param>
    /// <param name="mensagemHtml">Mensagem já em HTML — quem chama codifica o que veio do usuário.</param>
    /// <param name="botao">Texto do botão, ou nulo para nenhum.</param>
    /// <param name="link">Destino do botão.</param>
    /// <param name="comLogo">
    /// Abre com a marca da Kapa. Texto e não imagem: cliente de e-mail bloqueia imagem remota por
    /// padrão e descarta SVG embutido — o logo sumiria justamente no primeiro contato.
    /// </param>
    public static string Montar(string nomeDaAplicacao, string titulo, string mensagemHtml, string? botao, string? link, bool comLogo = false)
    {
        var marca = comLogo
            ? """<p style="font-size:26px;font-weight:800;letter-spacing:-0.02em;margin:0 0 24px"><span style="color:#F2994A">&#127891;</span> kapa</p>"""
            : string.Empty;

        var acao =
            botao is null || link is null
                ? string.Empty
                : $"""
                    <p style="margin:32px 0">
                      <a href="{link}" style="background:#111;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;display:inline-block">{Texto(
                        botao
                    )}</a>
                    </p>
                    <p style="color:#666;font-size:13px">Se o botão não funcionar, copie e cole este endereço no navegador:<br>{link}</p>
                    """;

        return $"""
            <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;max-width:520px;margin:0 auto;padding:24px;color:#111">
              {marca}
              <h1 style="font-size:20px;margin:0 0 16px">{Texto(titulo)}</h1>
              <p style="line-height:1.6;margin:0">{mensagemHtml}</p>
              {acao}
              <hr style="border:none;border-top:1px solid #eee;margin:32px 0">
              <p style="color:#999;font-size:12px;margin:0">{Texto(nomeDaAplicacao)} — mensagem automática, não responda.</p>
            </div>
            """;
    }
}
