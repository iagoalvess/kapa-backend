using Backend.Business.Recebimentos.Models;

namespace Backend.Business.Pagamentos.Models;

/// <summary>
/// Como se paga pelo Mercado Pago no Kapa — a parcela, a compra da loja e o plano (Sprint 35, decisão 1).
/// </summary>
/// <remarks>
/// É o único lugar que lista os meios: a tela de pagar monta as opções a partir do que o contexto passa
/// daqui. Os meios da comissão (chave PIX, transferência, dinheiro) são outro eixo, o
/// <c>MeioDeRecebimento</c>: não passam pelo Mercado Pago e baixam pela conferência.
/// <para>Só estes dois, por decisão de 25/09/2026 (Sprint 35, decisões 1 a 3). O Pix Automático saiu em
/// 28/09/2026: era mais uma integração (a recorrência do banco) para um ganho que o cartão recorrente e o
/// PIX avulso já cobrem. Gravado como texto.</para>
/// </remarks>
public enum MeioDePagamento
{
    /// <summary>PIX avulso, com valor e validade, gerado a cada pagamento.</summary>
    Pix,

    /// <summary>Cartão de crédito, tokenizado no navegador pelo SDK do Mercado Pago.</summary>
    Cartao,
}

/// <summary>O que cada meio oferece hoje e como se escreve.</summary>
public static class MeiosDePagamento
{
    /// <summary>Os meios que o Kapa sabe cobrar — o cartão entrou na Sprint 39. Cada turma oferece os seus (<see cref="DaTurma"/>).</summary>
    public static readonly IReadOnlyList<MeioDePagamento> Ligados = [MeioDePagamento.Pix, MeioDePagamento.Cartao];

    /// <summary>Em quantas vezes o formando e o comprador podem dividir no cartão; os juros são de quem paga (Sprint 39, P3).</summary>
    public const int ParcelasNoCartao = 12;

    /// <summary>
    /// Os meios que a turma oferece pelo Mercado Pago dela: nenhum sem conexão, o PIX com ela, e o cartão só
    /// quando a Tesouraria o ligou (Sprint 39, P7).
    /// </summary>
    /// <param name="credencial">A autorização da turma, ou nula.</param>
    public static IReadOnlyList<MeioDePagamento> DaTurma(CredencialDeProvedor? credencial) =>
        credencial is null ? []
        : credencial.CartaoLigado ? Ligados
        : [MeioDePagamento.Pix];

    /// <summary>Rótulo do meio, para e-mail, planilha e log.</summary>
    /// <param name="meio">Meio.</param>
    public static string Rotulo(MeioDePagamento meio) =>
        meio switch
        {
            MeioDePagamento.Pix => "PIX",
            _ => "Cartão de crédito",
        };
}
