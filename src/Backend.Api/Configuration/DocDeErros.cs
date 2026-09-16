using Backend.Business.Common;
using Microsoft.Extensions.Options;

namespace Backend.Api.Configuration;

/// <summary>
/// Monta o <c>type</c> do <c>ProblemDetails</c>: a doc do erro, com o código na âncora.
/// </summary>
/// <remarks>
/// A RFC 9457 define <c>type</c> como o identificador do <b>tipo de problema</b>, e é o campo que
/// leva quem lê ao que fazer a respeito. Apontá-lo para a definição do status HTTP devolve ao
/// cliente a informação que ele já tem em <c>status</c>.
/// <para>
/// Lido do <c>IOptions</c> a cada chamada, e não capturado numa estática: o endereço muda por
/// ambiente, e o <c>ProblemDetails</c> é montado tanto no <c>MainController</c> quanto no
/// <c>GlobalExceptionHandler</c>, que não compartilham nada além do <c>HttpContext</c>.
/// </para>
/// </remarks>
public static class DocDeErros
{
    /// <summary>Código dos erros que não vêm do domínio — o que o <c>GlobalExceptionHandler</c> trata.</summary>
    public const string Inesperado = "erro.inesperado";

    /// <summary>
    /// A URL da doc para este código de erro.
    /// </summary>
    /// <remarks>
    /// Resolve o contêiner com <c>GetService</c>, e não <c>GetRequiredService</c>: quem chama é o
    /// caminho de erro, inclusive o <c>GlobalExceptionHandler</c>, e uma resposta de falha não pode
    /// falhar de novo por não achar uma configuração. Sem contêiner — no teste, ou numa falha tão
    /// cedo que o escopo nem existe — vale o padrão da classe.
    /// </remarks>
    /// <param name="contexto">Requisição em curso, de onde sai a configuração.</param>
    /// <param name="codigo">Código no formato <c>recurso.motivo</c>.</param>
    public static string Para(HttpContext contexto, string codigo)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var url = contexto.RequestServices?.GetService<IOptions<AplicacaoSettings>>()?.Value.UrlDaDocDeErros;

        return $"{(string.IsNullOrWhiteSpace(url) ? new AplicacaoSettings().UrlDaDocDeErros : url).TrimEnd('/')}#{codigo}";
    }
}
