namespace Backend.Business.Festa.Models;

/// <summary>
/// Em que pé está um item da festa.
/// </summary>
/// <remarks>
/// <b>Nunca é gravado</b> (decisão 2): sai das despesas vinculadas a cada consulta, como "Atrasada"
/// sai do vencimento da despesa na Sprint 10. Campo de estado mutável é o que um dia deixa de ser
/// atualizado — e aqui quem olha a tela é a turma inteira.
/// </remarks>
public enum EstadoDoItem
{
    /// <summary>Sem nenhuma despesa vinculada: a comissão ainda não fechou.</summary>
    AContratar,

    /// <summary>Tem despesa vinculada, e nem tudo foi pago.</summary>
    Contratado,

    /// <summary>Todas as despesas vinculadas estão pagas.</summary>
    Pago,

    /// <summary>A turma desistiu. Sai do custo da festa; as despesas já pagas continuam no caixa.</summary>
    Cancelado,
}
