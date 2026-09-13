namespace Backend.Business.Formaturas.Models;

/// <summary>
/// Ciclo de vida da formatura.
/// </summary>
/// <remarks>
/// Gravado como texto, e não como número: o índice parcial de rascunho filtra por
/// <c>status = 'Rascunho'</c>, e um número ali mudaria de sentido no dia em que alguém
/// reordenasse o enum.
/// </remarks>
public enum StatusDaFormatura
{
    /// <summary>Criada, ainda não contratada. Só edita os próprios dados.</summary>
    Rascunho,

    /// <summary>Checkout iniciado. Mesmas permissões do rascunho.</summary>
    AguardandoPagamento,

    /// <summary>Paga. Tudo liberado.</summary>
    Ativa,

    /// <summary>Assinatura vencida ou cancelada. Leitura de tudo, nenhuma escrita.</summary>
    Suspensa,

    /// <summary>Ciclo encerrado. Leitura e exportação, por cinco anos.</summary>
    Encerrada,

    /// <summary>
    /// Rascunho desistido antes de pagar. Some da lista de todos: os vínculos são desativados.
    /// </summary>
    /// <remarks>
    /// Não é exclusão: aceites de convite e consentimentos que apontam para a turma são prova e
    /// ficam. E libera o criador para abrir outro rascunho, porque o índice único filtra
    /// <c>status = 'Rascunho'</c>.
    /// </remarks>
    Descartada,
}
