namespace Backend.Business.Formaturas.Models;

/// <summary>
/// Ciclo de vida da formatura.
/// </summary>
/// <remarks>
/// Gravado como texto, e não como número: o índice parcial some e volta conforme o produto, e um
/// número ali mudaria de sentido no dia em que alguém reordenasse o enum.
/// <para>
/// <b>Toda turma nasce <see cref="Ativa"/></b>, no plano gratuito. Até 18/09/2026 existiam ainda
/// <c>Rascunho</c> ("criada, não contratada") e <c>AguardandoPagamento</c> ("checkout iniciado"),
/// que faziam sentido enquanto contratar era a porta de entrada. Com o gratuito, não há mais espera:
/// quem segura o que a turma pode fazer é o <b>plano</b> — <c>LimiteDeFormandos</c> e os módulos —,
/// não o status. Os dois estados ficaram inalcançáveis e saíram.
/// </para>
/// </remarks>
public enum StatusDaFormatura
{
    /// <summary>Em uso. É como a turma nasce, e o plano diz o que ela alcança.</summary>
    Ativa,

    /// <summary>Assinatura vencida ou cancelada. Leitura de tudo, nenhuma escrita.</summary>
    Suspensa,

    /// <summary>Ciclo encerrado. Leitura e exportação, por cinco anos.</summary>
    Encerrada,

    /// <summary>
    /// Turma desistida antes de contratar. Some da lista de todos: os vínculos são desativados.
    /// </summary>
    /// <remarks>
    /// Não é exclusão: aceites de convite e consentimentos que apontam para a turma são prova e
    /// ficam. E libera o criador para abrir outra, porque o limite é de <b>uma turma não paga por
    /// conta</b> (<c>ExisteGratuitaCriadaPorDeTodasAsFormaturas</c>).
    /// <para>
    /// Só sai de <see cref="Ativa"/> e só enquanto <b>não houver assinatura</b> — quem já pagou
    /// encerra, não descarta. Quem guarda essa condição é <c>FormaturaService.Descartar</c>.
    /// </para>
    /// </remarks>
    Descartada,
}
