namespace Backend.Business.Pagamentos.Models;

/// <summary>
/// Como se paga pelo Mercado Pago no Kapa — a parcela, a compra da loja e o plano (Sprint 35, decisão 1).
/// </summary>
/// <remarks>
/// É o único lugar que lista os meios: a tela de pagar monta as opções a partir do que o contexto passa
/// daqui. Os meios da comissão (chave PIX, transferência, dinheiro) são outro eixo, o
/// <c>MeioDeRecebimento</c>: não passam pelo Mercado Pago e baixam pela conferência.
/// <para>Só estes três, por decisão de 25/09/2026 (Sprint 35, decisões 1 a 3). Gravado como texto.</para>
/// </remarks>
public enum MeioDePagamento
{
    /// <summary>PIX avulso, com valor e validade, gerado a cada pagamento.</summary>
    Pix,

    /// <summary>Pix Automático: a pessoa autoriza uma vez no banco e o débito sai a cada ciclo.</summary>
    PixAutomatico,

    /// <summary>Cartão de crédito, tokenizado no navegador pelo SDK do Mercado Pago.</summary>
    Cartao,
}

/// <summary>O que cada meio oferece hoje e como se escreve.</summary>
public static class MeiosDePagamento
{
    /// <summary>
    /// Os meios que já cobram de verdade. <c>ponytail:</c> cartão e Pix Automático entram aqui nas Sprints 37
    /// (planos) e 39 (comissão e loja), com o fluxo de cada um.
    /// </summary>
    public static readonly IReadOnlyList<MeioDePagamento> Ligados = [MeioDePagamento.Pix];

    /// <summary>Rótulo do meio, para e-mail, planilha e log.</summary>
    /// <param name="meio">Meio.</param>
    public static string Rotulo(MeioDePagamento meio) =>
        meio switch
        {
            MeioDePagamento.Pix => "PIX",
            MeioDePagamento.PixAutomatico => "Pix Automático",
            _ => "Cartão de crédito",
        };
}
