namespace Backend.Business.Festa.Models;

/// <summary>
/// Quem paga um item da festa.
/// </summary>
/// <remarks>
/// Decisão 14: nem todo item é de todo mundo — a foto e o álbum são o caso clássico. Os dois entram
/// no custo da festa, e a diferença está em <b>como o valor previsto é montado</b>: o rateado é um
/// total; o por formando é um preço vezes quantos a comissão espera que comprem.
/// <para>
/// Nos dois casos o dinheiro passa pela turma — ela paga o fornecedor e cobra de volta. É o que
/// mantém a barra da Página Inicial honesta: o que sobe o custo também sobe o arrecadado, e a conta
/// fecha em 100%. Item pago direto ao fornecedor, fora do Kapa, não existe aqui.
/// </para>
/// </remarks>
public enum TipoDeRateio
{
    /// <summary>A turma inteira paga, pelo plano de cobrança. O valor previsto é o total do contrato.</summary>
    Turma,

    /// <summary>Só quem quiser. O valor previsto é o preço de cada um, e a quantidade é a expectativa da comissão.</summary>
    PorFormando,
}
