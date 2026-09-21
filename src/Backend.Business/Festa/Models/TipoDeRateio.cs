namespace Backend.Business.Festa.Models;

/// <summary>
/// Quem paga um item da festa.
/// </summary>
/// <remarks>
/// Decisão 14: nem todo item é de todo mundo — a foto e o álbum são o caso clássico. Os dois entram
/// no custo da festa, e a diferença está em <b>como o valor previsto é montado</b>: o rateado é um
/// total; o por formando é um preço vezes quantos a comissão espera que comprem.
/// <para>
/// Nos dois casos o dinheiro passa pela turma — ela paga o fornecedor e cobra de volta. Item pago
/// direto ao fornecedor, por fora do Kapa, não existe aqui: ele subiria o custo da festa sem nunca
/// subir a arrecadação, e a turma ficaria eternamente atrasada numa conta que não é dela.
/// </para>
/// <para>
/// <b>O "cobra de volta" ainda não tem porta.</b> Parcela nasce na adesão, a partir de
/// <c>PlanoDeCobranca.ItensAtivos</c>, e o plano de quem já aderiu está congelado no snapshot — item
/// novo só alcança quem ainda não aderiu. Então <see cref="PorFormando"/> hoje sobe o custo e não
/// sobe o arrecadado, e o percentual da meta no Início fica pessimista nessa fatia. Não é erro de
/// conta:
/// a turma realmente paga o fotógrafo do caixa dela. É receita sem caminho.
/// </para>
/// <para>
/// Quem fecha o circuito é o pedido do formando (Sprint 19, decisão 11): um item de catálogo aponta
/// para o item da festa, o pedido vira parcela no nome de quem comprou, e o cartão troca
/// <c>QuantidadeEstimada</c> pela contagem de pedidos confirmados.
/// </para>
/// </remarks>
public enum TipoDeRateio
{
    /// <summary>A turma inteira paga, pelo plano de cobrança. O valor previsto é o total do contrato.</summary>
    Turma,

    /// <summary>Só quem quiser. O valor previsto é o preço de cada um, e a quantidade é a expectativa da comissão.</summary>
    PorFormando,
}
