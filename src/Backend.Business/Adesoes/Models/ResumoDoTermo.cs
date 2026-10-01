namespace Backend.Business.Adesoes.Models;

/// <summary>
/// O resumo (um ou dois parágrafos curtos) gerado por modelo de linguagem que a tela de adesão mostra com uma versão
/// do termo (Sprint 24).
/// </summary>
/// <remarks>
/// Tabela própria porque o termo recusa <c>UPDATE</c>: o resumo chega depois da publicação e não
/// tem como virar coluna dele. Chave no <see cref="TermoId"/> — um resumo por versão, e gerar duas
/// vezes é conflito de chave, não duplicata.
/// <para>
/// Não herda de <c>EntidadeDaFormatura</c>: a turma é a do termo, e toda leitura parte dele, que já
/// passa pelo filtro global. <b>Não</b> faz parte do que foi aceito — fora do hash da adesão, do PDF
/// e do e-mail (decisão 2). Sem coluna de estado: existe a linha, o resumo aparece (decisão 9).
/// </para>
/// </remarks>
public class ResumoDoTermo
{
    /// <summary>Versão do termo resumida.</summary>
    public Guid TermoId { get; init; }

    /// <summary>O texto como o modelo devolveu, aparado.</summary>
    public string Texto { get; init; } = string.Empty;
}
