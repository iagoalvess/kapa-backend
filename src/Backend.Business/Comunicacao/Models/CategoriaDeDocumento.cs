namespace Backend.Business.Comunicacao.Models;

/// <summary>
/// Em que gaveta do acervo o documento mora.
/// </summary>
/// <remarks>
/// Obrigatória: é o que impede o acervo de virar lixeira de arquivos (risco da sprint), e é por ela
/// que a tela agrupa. Valores explícitos pelo mesmo motivo de <see cref="Visibilidade"/>.
/// </remarks>
public enum CategoriaDeDocumento
{
    /// <summary>Ata de reunião.</summary>
    Ata = 1,

    /// <summary>Contrato com fornecedor.</summary>
    Contrato = 2,

    /// <summary>Orçamento recebido ou aprovado.</summary>
    Orcamento = 3,

    /// <summary>Regulamento, estatuto, regras da turma.</summary>
    Regulamento = 4,

    /// <summary>O que não cabe nas outras.</summary>
    Outros = 5,
}
