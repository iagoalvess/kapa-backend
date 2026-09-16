using System.Text.RegularExpressions;
using Backend.Business.Emails.Services;

namespace Backend.Business.Notificacoes.Services;

/// <summary>
/// As variáveis de um template e a troca delas pelo valor do destinatário.
/// </summary>
/// <remarks>
/// A lista é fechada e conferida <b>na gravação</b> (critério de aceite): variável digitada errada
/// vira e-mail com <c>{vencimeto}</c> no meio para oitenta pessoas, e descobrir isso no envio é
/// descobrir tarde demais.
/// <para>
/// O valor entra escapado, porque o corpo é HTML e nome de pessoa e de turma são texto controlado
/// por terceiro — a mesma razão de <see cref="ModeloDeEmail.Texto"/>.
/// </para>
/// </remarks>
public static partial class TemplateDeNotificacao
{
    /// <summary>Teto de caracteres do corpo — o mesmo que o editor mostra.</summary>
    public const int TamanhoMaximo = 2_000;

    /// <summary>Teto de caracteres do assunto.</summary>
    public const int TamanhoMaximoDoAssunto = 150;

    /// <summary>As variáveis que um template aceita.</summary>
    public static readonly IReadOnlyList<string> Variaveis = ["nome", "valor", "vencimento", "link", "formatura", "quantidade"];

    [GeneratedRegex(@"\{([^{}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Marcadores();

    /// <summary>As variáveis citadas no texto que não existem — vazio quando está tudo certo.</summary>
    /// <param name="texto">Assunto ou corpo, como a tesouraria escreveu.</param>
    public static IReadOnlyList<string> Desconhecidas(string? texto) =>
        texto is null
            ? []
            :
            [
                .. Marcadores()
                    .Matches(texto)
                    .Select(m => m.Groups[1].Value)
                    .Where(nome => !Variaveis.Contains(nome, StringComparer.Ordinal))
                    .Distinct(StringComparer.Ordinal),
            ];

    /// <summary>Troca as variáveis pelos valores, escapando cada um para o HTML.</summary>
    /// <param name="texto">Template gravado.</param>
    /// <param name="valores">Valor de cada variável; a que faltar vira texto vazio.</param>
    public static string Renderizar(string texto, IReadOnlyDictionary<string, string> valores) =>
        Marcadores().Replace(texto, correspondencia => ModeloDeEmail.Texto(Valor(valores, correspondencia.Groups[1].Value)));

    /// <summary>A mesma troca, sem escapar — para o assunto, que não é HTML.</summary>
    /// <param name="texto">Template gravado.</param>
    /// <param name="valores">Valor de cada variável.</param>
    public static string RenderizarTexto(string texto, IReadOnlyDictionary<string, string> valores) =>
        Marcadores().Replace(texto, correspondencia => Valor(valores, correspondencia.Groups[1].Value));

    private static string Valor(IReadOnlyDictionary<string, string> valores, string nome) => valores.GetValueOrDefault(nome, string.Empty);
}
