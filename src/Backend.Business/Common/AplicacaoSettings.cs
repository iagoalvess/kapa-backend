namespace Backend.Business.Common;

/// <summary>
/// Identidade da aplicação, usada no que chega ao usuário final.
/// </summary>
/// <remarks>
/// <see cref="UrlDoFrontend"/> é o que transforma um token em link clicável. O back-end não tem
/// tela: ele emite o token e monta a URL da aplicação que vai consumi-lo. Sem isso configurado,
/// o e-mail de redefinição de senha chega com um link quebrado.
/// </remarks>
public sealed class AplicacaoSettings
{
    /// <summary>Nome da seção correspondente no arquivo de configuração.</summary>
    public const string Secao = "Aplicacao";

    /// <summary>Nome exibido nos e-mails.</summary>
    public string Nome { get; init; } = "Aplicação";

    /// <summary>Endereço base do app (<c>app.kapaformaturas.com.br</c>), sem barra no final.</summary>
    /// <remarks>É de onde saem todos os links de tela dos e-mails: confirmar e-mail, convite, recibo, compra.</remarks>
    public string UrlDoFrontend { get; init; } = "http://localhost:3000";

    /// <summary>Endereço base do site (<c>kapaformaturas.com.br</c>), sem barra no final.</summary>
    /// <remarks>
    /// O site é a página institucional e os documentos legais, separado do app na Sprint 33 (P3). O
    /// que aponta para ele — a Política de Privacidade no rodapé do e-mail — monta a URL com
    /// <see cref="LinkDoSite"/>.
    /// </remarks>
    public string UrlDoSite { get; init; } = "http://localhost:5180";

    /// <summary>
    /// Onde a documentação dos erros está publicada, sem barra no final.
    /// </summary>
    /// <remarks>
    /// É o <c>type</c> de todo <c>ProblemDetails</c>, com o código do erro na âncora:
    /// <c>{UrlDaDocDeErros}#pagamento.parcela_paga</c>. O campo <c>type</c> existe na RFC 9457 para
    /// levar a quem lê ao significado do erro — apontando para a definição genérica do status HTTP
    /// ele não diz nada que o próprio <c>status</c> já não diga.
    /// <para>
    /// O conteúdo vive em <c>docs/erros.md</c>, no repositório. Publicar é apontar esta chave para
    /// o endereço onde ele foi publicado, sem tocar em código.
    /// </para>
    /// </remarks>
    public string UrlDaDocDeErros { get; init; } = "https://github.com/kapa/backend/blob/main/docs/erros.md";

    /// <summary>Monta a URL de uma tela do front-end.</summary>
    /// <param name="caminho">Caminho da rota no front, começando com barra — de preferência um de <see cref="RotasDoFront"/>.</param>
    public string Link(string caminho) => $"{UrlDoFrontend.TrimEnd('/')}{caminho}";

    /// <summary>Monta a URL de uma página do site.</summary>
    /// <param name="caminho">Caminho da página, começando com barra — um de <see cref="RotasDoSite"/>.</param>
    public string LinkDoSite(string caminho) => $"{UrlDoSite.TrimEnd('/')}{caminho}";

    /// <summary>Monta uma URL do front-end a partir de um caminho e de parâmetros de consulta.</summary>
    /// <param name="caminho">Caminho da rota no front, começando com barra.</param>
    /// <param name="parametros">Pares de chave e valor para a query string.</param>
    public string MontarUrl(string caminho, IEnumerable<KeyValuePair<string, string>> parametros)
    {
        var consulta = string.Join('&', parametros.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        return $"{Link(caminho)}?{consulta}";
    }
}
