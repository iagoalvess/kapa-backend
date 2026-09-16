namespace Backend.Business.Pagamentos.Models;

/// <summary>Como o dinheiro chegou à conta da turma.</summary>
/// <remarks>Gravado como texto: renomear um valor aqui é migration, não refatoração.</remarks>
public enum FormaDePagamento
{
    /// <summary>PIX para a chave da comissão — o caminho do QR.</summary>
    Pix,

    /// <summary>Dinheiro em mãos, entregue à tesouraria.</summary>
    Dinheiro,

    /// <summary>TED ou DOC.</summary>
    Transferencia,

    /// <summary>Qualquer outro meio.</summary>
    Outro,
}
