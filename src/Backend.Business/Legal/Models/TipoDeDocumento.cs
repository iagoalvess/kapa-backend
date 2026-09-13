namespace Backend.Business.Legal.Models;

/// <summary>
/// Documentos legais que toda conta precisa aceitar.
/// </summary>
/// <remarks>
/// Constantes, e não enum: o valor viaja na URL (<c>/legal/TermosDeUso/1</c>) e no corpo do
/// cadastro, e texto estável é contrato mais legível que número.
/// </remarks>
public static class TipoDeDocumento
{
    /// <summary>Termos de Uso da plataforma.</summary>
    public const string TermosDeUso = nameof(TermosDeUso);

    /// <summary>Política de Privacidade da plataforma.</summary>
    public const string PoliticaDePrivacidade = nameof(PoliticaDePrivacidade);

    /// <summary>Todos os tipos. O cadastro exige aceite de cada um.</summary>
    public static readonly IReadOnlyList<string> Todos = [TermosDeUso, PoliticaDePrivacidade];

    /// <summary>
    /// Devolve a grafia oficial do tipo, aceitando qualquer caixa.
    /// </summary>
    /// <param name="tipo">Tipo como veio da requisição.</param>
    /// <returns>O tipo oficial, ou nulo se não for um documento conhecido.</returns>
    public static string? Normalizar(string? tipo) =>
        Todos.FirstOrDefault(conhecido => string.Equals(conhecido, tipo, StringComparison.OrdinalIgnoreCase));
}
