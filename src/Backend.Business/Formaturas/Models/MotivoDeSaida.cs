namespace Backend.Business.Formaturas.Models;

/// <summary>
/// Por que alguém deixou a turma.
/// </summary>
/// <remarks>
/// Lista fechada (P6 de 17/09/2026), gravada como texto: dois anos depois, "por que o João saiu" é
/// pergunta de assembleia de prestação de contas, e agrupar respostas livres não responde nada.
/// <para>
/// <see cref="Outro"/> é a válvula de escape, e só ele aceita — e exige — a justificativa em
/// <c>VinculoDeFormatura.DetalheDoDesligamento</c>. Guardar o texto livre na própria coluna do
/// motivo faria "Outro" sumir do agrupamento.
/// </para>
/// </remarks>
public static class MotivoDeSaida
{
    /// <summary>Trancou a matrícula.</summary>
    public const string Trancamento = nameof(Trancamento);

    /// <summary>Transferiu-se para outra instituição.</summary>
    public const string Transferencia = nameof(Transferencia);

    /// <summary>Vai se formar, mas com outra turma.</summary>
    public const string FormaturaEmOutraTurma = nameof(FormaturaEmOutraTurma);

    /// <summary>Continua no curso, mas não vai à festa.</summary>
    public const string DesistenciaDaFesta = nameof(DesistenciaDaFesta);

    /// <summary>Não consegue sustentar as parcelas.</summary>
    public const string DificuldadeFinanceira = nameof(DificuldadeFinanceira);

    /// <summary>Qualquer outro motivo. Exige a justificativa por escrito.</summary>
    public const string Outro = nameof(Outro);

    /// <summary>Todos os motivos aceitos.</summary>
    public static readonly IReadOnlyList<string> Todos =
    [
        Trancamento,
        Transferencia,
        FormaturaEmOutraTurma,
        DesistenciaDaFesta,
        DificuldadeFinanceira,
        Outro,
    ];
}
