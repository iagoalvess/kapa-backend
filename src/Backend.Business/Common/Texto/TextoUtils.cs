using System.Globalization;
using System.Text;

namespace Backend.Business.Common.Texto;

/// <summary>
/// Normalização e mascaramento de texto.
/// </summary>
public static class TextoUtils
{
    /// <summary>
    /// Mascara um e-mail para uso em log.
    /// </summary>
    /// <remarks>
    /// E-mail é dado pessoal: log de tentativa de login não pode virar lista de endereços
    /// válidos para quem tiver acesso ao agregador. <c>joao.silva@exemplo.com</c> vira
    /// <c>j*******a@exemplo.com</c> — o suficiente para correlacionar, insuficiente para vazar.
    /// </remarks>
    /// <param name="email">Endereço a mascarar.</param>
    public static string MascararEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "(vazio)";

        var arroba = email.IndexOf('@', StringComparison.Ordinal);
        if (arroba <= 0)
            return "(invalido)";

        var usuario = email[..arroba];
        var dominio = email[arroba..];

        if (usuario.Length <= 2)
            return $"{usuario[0]}*{dominio}";

        return $"{usuario[0]}{new string('*', usuario.Length - 2)}{usuario[^1]}{dominio}";
    }

    /// <summary>
    /// Remove acentuação e baixa a caixa, para comparação e busca insensíveis a acento.
    /// </summary>
    /// <param name="texto">Texto de origem.</param>
    public static string Normalizar(string? texto) => SemAcento(texto).ToLowerInvariant();

    /// <summary>
    /// Remove a acentuação e as pontas em branco, mantendo a caixa: <c>" São João "</c> vira <c>"Sao Joao"</c>.
    /// </summary>
    /// <param name="texto">Texto de origem.</param>
    public static string SemAcento(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        var decomposto = texto.Trim().Normalize(NormalizationForm.FormD);
        var construtor = new StringBuilder(decomposto.Length);

        foreach (var caractere in decomposto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(caractere) != UnicodeCategory.NonSpacingMark)
                construtor.Append(caractere);
        }

        return construtor.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Corta o texto no tamanho da coluna. Para dado de auditoria que vem do cliente (User-Agent).</summary>
    /// <remarks>Navegador embutido (Instagram, Facebook) manda User-Agent de 600+ caracteres: gravar inteiro estoura a coluna e recusa a operação.</remarks>
    /// <param name="texto">Texto de origem.</param>
    /// <param name="maximo">Tamanho máximo.</param>
    public static string? Truncar(string? texto, int maximo) => texto is null || texto.Length <= maximo ? texto : texto[..maximo];

    /// <summary>
    /// <c>NovaSenha</c> vira <c>nova_senha</c> — a convenção de nomes da API.
    /// </summary>
    /// <remarks>
    /// Um lugar só para a regra, porque ela aparece em três: o nome do campo que o FluentValidation
    /// devolve, o código de erro que o Identity gera e o nome que o model binder procura na query.
    /// Três cópias divergiriam no primeiro caso de borda — e o caso de borda aqui é o índice de
    /// coleção, que atravessa intacto.
    /// <para>
    /// O ponto separa níveis de um caminho aninhado (<c>Itens[0].ValorEmCentavos</c>), e cada nível
    /// é convertido por conta própria.
    /// </para>
    /// </remarks>
    /// <param name="texto">Nome em PascalCase ou camelCase.</param>
    public static string ParaSnakeCase(string? texto) =>
        string.IsNullOrEmpty(texto) ? texto ?? string.Empty : string.Join('.', texto.Split('.').Select(Segmento));

    private static string Segmento(string segmento)
    {
        var colchete = segmento.IndexOf('[', StringComparison.Ordinal);
        var nome = colchete < 0 ? segmento : segmento[..colchete];
        var sufixo = colchete < 0 ? string.Empty : segmento[colchete..];

        var texto = new StringBuilder(nome.Length + 8);

        for (var i = 0; i < nome.Length; i++)
        {
            if (char.IsUpper(nome[i]) && i > 0)
                texto.Append('_');

            texto.Append(char.ToLowerInvariant(nome[i]));
        }

        return texto.Append(sufixo).ToString();
    }
}
