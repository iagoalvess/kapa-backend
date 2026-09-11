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

    /// <summary>Endereço base do front-end, sem barra no final.</summary>
    public string UrlDoFrontend { get; init; } = "http://localhost:3000";

    /// <summary>Monta uma URL do front-end a partir de um caminho e de parâmetros de consulta.</summary>
    /// <param name="caminho">Caminho da rota no front, começando com barra.</param>
    /// <param name="parametros">Pares de chave e valor para a query string.</param>
    public string MontarUrl(string caminho, IEnumerable<KeyValuePair<string, string>> parametros)
    {
        var consulta = string.Join('&', parametros.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        return $"{UrlDoFrontend.TrimEnd('/')}{caminho}?{consulta}";
    }
}
