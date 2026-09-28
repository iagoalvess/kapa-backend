using System.Text.RegularExpressions;
using Backend.Business.Emails.Services;

namespace Backend.Business.Notificacoes.Services;

/// <summary>
/// As variáveis dos textos da régua e a troca delas pelo valor do destinatário.
/// </summary>
/// <remarks>
/// Os textos são do Kapa (<c>ReguaDoKapa</c>); a lista fechada de variáveis existe para o teste do
/// catálogo pegar a <c>{vencimeto}</c> digitada errada antes de ela sair para oitenta pessoas.
/// <para>
/// O valor entra escapado, porque o corpo é HTML e nome de pessoa e de turma são texto controlado por
/// terceiro — a mesma razão de <see cref="ModeloDeEmail.Texto"/>.
/// </para>
/// </remarks>
public static partial class TemplateDeNotificacao
{
    /// <summary>As variáveis que um texto da régua aceita.</summary>
    public static readonly IReadOnlyList<string> Variaveis = ["nome", "valor", "vencimento", "formatura", "quantidade"];

    [GeneratedRegex(@"\{([^{}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Marcadores();

    /// <summary>As variáveis citadas no texto que não existem — vazio quando está tudo certo.</summary>
    /// <param name="texto">Assunto ou corpo.</param>
    public static IReadOnlyList<string> Desconhecidas(string texto) =>
        [
            .. Marcadores()
                .Matches(texto)
                .Select(m => m.Groups[1].Value)
                .Where(nome => !Variaveis.Contains(nome, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal),
        ];

    /// <summary>Troca as variáveis pelos valores e escapa o resultado inteiro para o HTML.</summary>
    /// <param name="texto">Texto do catálogo.</param>
    /// <param name="valores">Valor de cada variável; a que faltar vira texto vazio.</param>
    public static string Renderizar(string texto, IReadOnlyDictionary<string, string> valores) =>
        ModeloDeEmail.Texto(Marcadores().Replace(texto, correspondencia => valores.GetValueOrDefault(correspondencia.Groups[1].Value, string.Empty)));
}
