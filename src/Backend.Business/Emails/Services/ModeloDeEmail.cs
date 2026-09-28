using System.Net;
using System.Text.RegularExpressions;
using Backend.Business.Common;
using Backend.Business.Common.Pdf;

namespace Backend.Business.Emails.Services;

/// <summary>
/// O mascote que abre o e-mail. O nome é o do arquivo em <c>Emails/Recursos</c>, em minúsculas.
/// </summary>
/// <remarks>
/// Enum e não string: mascote digitado errado viraria imagem quebrada no topo de oitenta mensagens,
/// e isso só aparece na caixa de quem recebeu.
/// </remarks>
public enum Mascote
{
    /// <summary>Comemorando. O padrão — serve a qualquer aviso que não seja ruim.</summary>
    Feliz,

    /// <summary>Acenando. Boas-vindas, convite, primeira mensagem.</summary>
    Acenando,

    /// <summary>De alerta, com a engrenagem quebrada. Algo deu errado e pede ação: estorno, assinatura vencida.</summary>
    /// <remarks>O desenho lê como falha: aviso de segurança usa <see cref="Cadeado"/>, e troca a conferir, <see cref="Lupa"/>.</remarks>
    Alerta,

    /// <summary>Com o cadeado. Segurança: senha, link de acesso, pedido que só o titular pode fazer.</summary>
    Cadeado,

    /// <summary>De binóculo. Procurando alguém — o lembrete de quem ainda não apareceu.</summary>
    Binoculo,

    /// <summary>Com o canudo. Formatura, adesão fechada, fim de ciclo.</summary>
    Canudo,

    /// <summary>Com o celular. Código de verificação.</summary>
    Celular,

    /// <summary>Com a lista. Pendência a cumprir.</summary>
    Checklist,

    /// <summary>Com o cofrinho. Dinheiro: parcela paga, recibo, extrato.</summary>
    Cofrinho,

    /// <summary>Com cara de erro. Pagamento recusado, falha, desligamento.</summary>
    Erro,

    /// <summary>No foguete. Turma ativada, licença confirmada.</summary>
    Foguete,

    /// <summary>Lendo um documento. Termo, relatório, exportação de dados.</summary>
    Documento,

    /// <summary>Lendo. Aviso do mural, comunicado da comissão.</summary>
    Lendo,

    /// <summary>Com a lupa. Conferência, auditoria, algo a verificar.</summary>
    Lupa,
}

/// <summary>
/// O esqueleto HTML comum a todo e-mail da aplicação: marca, mascote, título, mensagem, botão e rodapé.
/// </summary>
/// <remarks>
/// HTML montado em código, sem motor de template — ver <c>EmailsDeConta</c>. Tudo o que vem do
/// usuário passa por <see cref="Texto"/> antes de entrar no corpo: nome de turma e de pessoa é
/// texto controlado por terceiro, e e-mail é HTML.
/// <para>
/// O desenho é de uma coluna centrada: mascote numa faixa clara no topo do cartão, título grande,
/// texto e botão no eixo. É o que o Gmail mostra num celular sem precisar de media query — e
/// media query é justamente o que metade dos clientes de e-mail ignora.
/// </para>
/// <para>
/// O visual segue os tokens do frontend (<c>styles/index.css</c>): fundo creme <c>--page</c>,
/// cartão branco com borda <c>--line</c> e cantos de <c>--radius</c>, CTA no laranja da marca e
/// texto em <c>--text-primary</c>/<c>--text-muted</c>. Valores literais, e não variáveis CSS:
/// cliente de e-mail não lê <c>var()</c> — mudou a paleta lá, muda aqui.
/// </para>
/// <para>
/// Layout em tabela e estilo em atributo <c>style</c>, que é o que o Outlook (motor do Word)
/// entende: <c>&lt;div&gt;</c> com flex ou folha de estilo externa sai desmontado nele. Cantos
/// arredondados ele ignora — o cartão vira quadrado, e só.
/// </para>
/// <para>
/// O corpo cita o logo e o mascote por <c>cid:</c>, e quem decide como eles chegam é o
/// <see cref="SmtpEmailSender"/>, na hora de entregar. Em produção (<c>Smtp:UrlDasImagens</c>
/// preenchida) o <c>cid:</c> vira a URL pública da API, pelo <see cref="ComImagensPorUrl"/> — o padrão
/// de mercado, que deixa a mensagem leve e não faz o Gmail listar as imagens como anexos "noname". Em
/// desenvolvimento, sem URL que uma caixa de e-mail alcance (<c>localhost</c>), elas vão anexadas, por
/// <see cref="ImagensDe"/>. A fila guarda o corpo com <c>cid:</c>, então a escolha não depende de onde a
/// mensagem foi enfileirada. Imagem remota é bloqueada pelo Outlook até a pessoa liberar: nada do
/// texto depende dela, e o logo tem <c>alt</c>.
/// </para>
/// <para>
/// O logo é <b>PNG</b>, e não o SVG do front: Gmail, Outlook e Yahoo não desenham SVG em e-mail.
/// <c>Emails/Recursos/logo.png</c> é o <c>LogoKapa</c> rasterizado com a Plus Jakarta Sans 800 — a
/// fonte da marca não existe na máquina de quem lê, e escrever "kapa" em Arial era a marca errada.
/// Mudou o logo lá, gere este de novo.
/// </para>
/// </remarks>
public static partial class ModeloDeEmail
{
    /// <summary>Pilha de fontes: só o que já existe na máquina de quem lê — webfont não passa no Gmail nem no Outlook.</summary>
    private const string Fonte = "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";

    /// <summary>Prefixo do <c>Content-ID</c> de toda imagem nossa, para não colidir com nada do cliente.</summary>
    private const string Prefixo = "kapa-";

    [GeneratedRegex($@"cid:{Prefixo}(?<nome>[a-z]+)", RegexOptions.CultureInvariant)]
    private static partial Regex Referencias();

    /// <summary>Codifica texto para entrar no HTML.</summary>
    /// <param name="valor">Texto cru.</param>
    public static string Texto(string? valor) => WebUtility.HtmlEncode(valor ?? string.Empty);

    /// <summary>
    /// As imagens citadas no corpo, para quem for entregar a mensagem anexar.
    /// </summary>
    /// <remarks>
    /// Lê o próprio HTML em vez de receber a lista pronta: assim o dia em que um e-mail ganhar uma
    /// segunda imagem não mexe no contrato de <c>IEmailSender</c> nem na fila, que guarda só o corpo.
    /// </remarks>
    /// <param name="corpoHtml">Corpo montado por <see cref="Montar"/>.</param>
    /// <returns>O <c>Content-ID</c> e o conteúdo de cada imagem, sem repetir.</returns>
    public static IEnumerable<(string Cid, Stream Conteudo)> ImagensDe(string corpoHtml) =>
        Referencias()
            .Matches(corpoHtml)
            .Select(correspondencia => correspondencia.Groups["nome"].Value)
            .Distinct(StringComparer.Ordinal)
            .Select(nome => (Prefixo + nome, Conteudo: Recurso(nome)))
            .Where(imagem => imagem.Conteudo is not null)
            .Select(imagem => (imagem.Item1, imagem.Conteudo!));

    /// <summary>
    /// Troca cada <c>cid:</c> das nossas imagens pela URL pública, com a versão do conteúdo.
    /// </summary>
    /// <remarks>
    /// <c>{urlBase}/logo.png?v=1a2b3c4d</c>: a versão é o começo do hash do PNG, e a rota responde com cache
    /// de um ano. Imagem que não existe no assembly fica como estava — o <c>cid:</c> sem anexo vira um
    /// quadrado vazio, o mesmo que já aconteceria hoje.
    /// </remarks>
    /// <param name="corpoHtml">Corpo montado por <see cref="Montar"/>.</param>
    /// <param name="urlBase">Endereço da rota das imagens, sem a barra final.</param>
    public static string ComImagensPorUrl(string corpoHtml, string urlBase) =>
        Referencias()
            .Replace(
                corpoHtml,
                referencia =>
                {
                    var nome = referencia.Groups["nome"].Value;

                    return RecursosDaMarca.Versao(nome) is { } versao ? $"{urlBase.TrimEnd('/')}/{nome}.png?v={versao}" : referencia.Value;
                }
            );

    /// <summary>Monta o corpo do e-mail.</summary>
    /// <param name="aplicacao">Nome exibido no rodapé e o endereço do site, para onde aponta a Política de Privacidade.</param>
    /// <param name="titulo">Título, codificado aqui.</param>
    /// <param name="mensagemHtml">Mensagem já em HTML — quem chama codifica o que veio do usuário.</param>
    /// <param name="botao">Texto do botão, ou nulo para nenhum.</param>
    /// <param name="link">Destino do botão.</param>
    /// <param name="mascote">Qual mascote abre a mensagem.</param>
    /// <remarks>
    /// Sem "se o botão não funcionar, copie este endereço": o botão é um <c>&lt;a href&gt;</c>, e ele
    /// funciona. A linha só emprestava a um e-mail nosso a cara de um phishing, que é quem precisa
    /// pedir que a pessoa cole uma URL na barra do navegador.
    /// </remarks>
    public static string Montar(
        AplicacaoSettings aplicacao,
        string titulo,
        string mensagemHtml,
        string? botao,
        string? link,
        Mascote mascote = Mascote.Feliz
    )
    {
        var acao =
            botao is null || link is null
                ? string.Empty
                : $"""
                    <table role="presentation" cellpadding="0" cellspacing="0" border="0" align="center" style="margin:32px auto 0">
                      <tr>
                        <td align="center" bgcolor="#f2994a" style="border-radius:999px">
                          <a href="{link}" style="display:inline-block;padding:15px 36px;font-family:{Fonte};font-size:15px;font-weight:700;line-height:20px;color:#ffffff;text-decoration:none;border-radius:999px">{Texto(
                            botao
                        )}</a>
                        </td>
                      </tr>
                    </table>
                    """;

        return $"""
            <!doctype html>
            <html lang="pt-BR">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <meta name="color-scheme" content="light">
            <meta name="supported-color-schemes" content="light">
            <title>{Texto(titulo)}</title>
            </head>
            <body style="margin:0;padding:0;background-color:#f7f6f3">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#f7f6f3">
              <tr>
                <td align="center" style="padding:40px 16px">
                  <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:600px">
                    <tr>
                      <td align="center" style="padding:0 0 22px">
                        <img src="cid:{Prefixo}logo" width="150" height="40" alt="Kapa" style="display:block;width:150px;height:40px;border:0">
                      </td>
                    </tr>
                    <tr>
                      <td style="background-color:#ffffff;border:1px solid #e8e7e3;border-radius:16px">
                        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                          <tr>
                            <td align="center" bgcolor="#fbf1e7" style="padding:28px 24px;border-radius:16px 16px 0 0">
                              <img src="cid:{Prefixo}{Arquivo(mascote)}" width="132" height="132" alt="" style="display:block;width:132px;height:132px;border:0">
                            </td>
                          </tr>
                          <tr>
                            <td align="center" style="padding:36px 40px 40px">
                              <h1 style="margin:0 0 14px;font-family:{Fonte};font-size:26px;font-weight:800;letter-spacing:-0.02em;line-height:32px;color:#1a1a18;text-align:center">{Texto(
                                  titulo
                              )}</h1>
                              <p style="margin:0;font-family:{Fonte};font-size:15px;line-height:25px;color:#55554f;text-align:center">{mensagemHtml}</p>
                              {acao}
                            </td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                    <tr>
                      <td style="padding:28px 0 0">
                        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                          <tr>
                            <td height="4" bgcolor="#f2994a" style="height:4px;line-height:4px;font-size:0;border-radius:2px">&nbsp;</td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                    <tr>
                      <td align="center" style="padding:18px 16px 0;font-family:{Fonte};font-size:12px;line-height:19px;color:#9a9a94;text-align:center">{Texto(
                          aplicacao.Nome
                      )} — mensagem automática, não responda.<br><a href="{aplicacao.LinkDoSite(RotasDoSite.Privacidade)}" style="color:#9a9a94;text-decoration:underline">Política de Privacidade</a></td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
            </body>
            </html>
            """;
    }

    /// <summary>O arquivo do mascote, que é o nome dele em minúsculas.</summary>
    public static string Arquivo(Mascote mascote) => mascote.ToString().ToLowerInvariant();

    /// <summary>O PNG embutido no assembly, ou nulo se alguém tiver removido o arquivo.</summary>
    private static Stream? Recurso(string nome) => RecursosDaMarca.Abrir(nome);
}
