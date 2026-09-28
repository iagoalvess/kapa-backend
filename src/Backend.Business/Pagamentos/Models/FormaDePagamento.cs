using Backend.Business.Recebimentos.Models;

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

    /// <summary>Cartão de crédito pelo Mercado Pago da turma (Sprint 35) — a baixa automática do cartão chega na Sprint 39.</summary>
    Cartao,
}

/// <summary>A ponte entre os dois eixos: o meio que o formando escolheu e a forma que a baixa grava.</summary>
/// <remarks>
/// São eixos separados de propósito — <see cref="MeioDeRecebimento"/> é o que o Kapa oferece antes do
/// pagamento, <see cref="FormaDePagamento"/> é o que se registra depois, e está gravada como texto em
/// todo recebimento desde a Sprint 9. Esta função é o único lugar que os relaciona.
/// </remarks>
public static class FormasDePagamento
{
    /// <summary>A forma correspondente ao meio escolhido no aviso (decisão 4 da Sprint 18).</summary>
    /// <remarks>
    /// Sem meio é PIX: é o que a conferência já gravava antes da Sprint 18, e todo aviso anterior a
    /// ela foi de fato um PIX — era o único caminho que o produto oferecia.
    /// </remarks>
    /// <param name="meio">Meio escolhido, ou nulo nos avisos antigos.</param>
    public static FormaDePagamento Da(MeioDeRecebimento? meio) =>
        meio switch
        {
            MeioDeRecebimento.Transferencia => FormaDePagamento.Transferencia,
            MeioDeRecebimento.Dinheiro => FormaDePagamento.Dinheiro,
            MeioDeRecebimento.Outro => FormaDePagamento.Outro,
            _ => FormaDePagamento.Pix,
        };

    /// <summary>A forma que a baixa automática grava para o que o Mercado Pago cobrou.</summary>
    /// <param name="meio">Meio da cobrança.</param>
    public static FormaDePagamento Da(MeioDePagamento meio) => meio == MeioDePagamento.Cartao ? FormaDePagamento.Cartao : FormaDePagamento.Pix;

    /// <summary>A forma como a pessoa a escreve — o que o recibo imprime.</summary>
    /// <param name="forma">Forma gravada.</param>
    public static string Rotulo(FormaDePagamento forma) =>
        forma switch
        {
            FormaDePagamento.Pix => "PIX",
            FormaDePagamento.Dinheiro => "Dinheiro",
            FormaDePagamento.Transferencia => "Transferência",
            FormaDePagamento.Cartao => "Cartão de crédito",
            _ => "Outro",
        };
}
