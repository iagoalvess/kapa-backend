using Backend.Business.Abstractions;

namespace Backend.Business.Cobrancas.Models;

/// <summary>
/// O que um formando pediu só para ele: um convite a mais, o kit, a foto.
/// </summary>
/// <remarks>
/// Um pedido por item por formando (Sprint 20, decisão 3), e o que muda é a quantidade — não existe
/// carrinho. É isso que mantém a chave natural da parcela, <c>(VinculoId, ItemDeCobrancaId,
/// Numero)</c>, funcionando sem alteração: dois pedidos do mesmo item colidiriam no número, e a
/// saída seria mexer no índice único que protege a turma inteira de cobrança duplicada.
/// <para>
/// Não há fila nem aprovação (P2): o pedido nasce <see cref="StatusDoPedido.Confirmado"/>, com as
/// parcelas já gravadas. Quem devolve o estoque é o cancelamento, e ele é humano — não há relógio
/// nesta sprint (decisão 9).
/// </para>
/// </remarks>
public class Pedido : EntidadeDaFormatura
{
    /// <summary>Vínculo de quem pediu.</summary>
    public Guid VinculoId { get; set; }

    /// <summary>Item opcional pedido.</summary>
    public Guid ItemDeCobrancaId { get; set; }

    /// <summary>Quantas unidades. Sempre ao menos uma enquanto o pedido está confirmado.</summary>
    public int Quantidade { get; private set; }

    /// <summary>
    /// Em quantas vezes o formando escolheu pagar, de 1 (à vista) até o teto do item.
    /// </summary>
    /// <remarks>
    /// Fica no pedido, e não na parcela, porque aumentar a quantidade depois gera um bloco novo com a
    /// mesma divisão — o formando escolhe uma vez. Quem quer adiantar paga várias parcelas de uma vez.
    /// </remarks>
    public int Parcelas { get; private set; } = 1;

    /// <summary>Situação. Muda só por <see cref="Cancelar"/>.</summary>
    public StatusDoPedido Status { get; private set; } = StatusDoPedido.Confirmado;

    /// <summary>Quando foi pedido, em UTC.</summary>
    public DateTime PedidoEm { get; private set; } = DateTime.UtcNow;

    /// <summary>Quando foi cancelado, em UTC. Nulo no pedido de pé.</summary>
    public DateTime? CanceladoEm { get; private set; }

    /// <summary>Detalhe livre de quem pede — tamanho da beca, nome no convite (Sprint 48, D26). A Gestão lê na lista.</summary>
    public string? Observacao { get; private set; }

    /// <summary>Se ainda vale — é o que conta no estoque e o que a Sprint 21 vai transformar em convite.</summary>
    public bool Confirmado => Status == StatusDoPedido.Confirmado;

    /// <summary>Um pedido novo, já confirmado.</summary>
    /// <param name="vinculoId">Quem pediu.</param>
    /// <param name="itemId">Item opcional.</param>
    /// <param name="quantidade">Quantas unidades.</param>
    /// <param name="parcelas">Em quantas vezes, já conferido contra o teto do item.</param>
    /// <param name="observacao">Detalhe livre de quem pede.</param>
    public static Pedido Novo(Guid vinculoId, Guid itemId, int quantidade, int parcelas = 1, string? observacao = null) =>
        new()
        {
            VinculoId = vinculoId,
            ItemDeCobrancaId = itemId,
            Quantidade = quantidade,
            Parcelas = parcelas,
            Observacao = Aparar(observacao),
        };

    /// <summary>Troca o detalhe livre. Nulo mantém o que havia — o <c>PUT</c> da quantidade não o reenvia.</summary>
    /// <param name="observacao">Texto novo; vazio apaga.</param>
    public void Observar(string? observacao)
    {
        if (observacao is not null)
            Observacao = Aparar(observacao);
    }

    private static string? Aparar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>Grava a quantidade nova — sempre absoluta, nunca um incremento.</summary>
    /// <remarks>
    /// Absoluta de propósito (decisão 10): repetir o mesmo <c>PUT</c> reserva delta zero e é um
    /// no-op, o que dispensa chave de idempotência para um erro que o índice único já cobre.
    /// <para>
    /// Pedir de novo o que já se cancelou reaproveita a linha: o índice único (vínculo, item) é o
    /// que faz do clique duplo um pedido só, e ele não distingue cancelado de confirmado.
    /// </para>
    /// </remarks>
    /// <param name="quantidade">Quantidade final.</param>
    /// <param name="parcelas">
    /// A divisão nova, só quando o pedido volta de um cancelamento — no pedido de pé ela não muda.
    /// </param>
    public void Ajustar(int quantidade, int? parcelas = null)
    {
        if (!Confirmado && parcelas is not null)
            Parcelas = parcelas.Value;

        Quantidade = quantidade;
        Status = StatusDoPedido.Confirmado;
        CanceladoEm = null;
    }

    /// <summary>
    /// Cancela o pedido, ou o reduz ao que o dinheiro já pago cobre.
    /// </summary>
    /// <remarks>
    /// <paramref name="quantidadeMantida"/> é o <c>piso(pago ÷ preço unitário)</c> da P9: quem pagou
    /// uma de duas parcelas de dois convites fica com um, e o outro volta ao estoque. Zero cancela o
    /// pedido inteiro. Cancelar de novo não muda nada — é o que faz o clique repetido não devolver
    /// estoque duas vezes.
    /// </remarks>
    /// <param name="quantidadeMantida">Unidades que o pedido conserva.</param>
    /// <returns>Quantas unidades voltam ao estoque.</returns>
    public int Cancelar(int quantidadeMantida)
    {
        if (!Confirmado)
            return 0;

        var devolvidas = Quantidade - quantidadeMantida;

        if (quantidadeMantida > 0)
        {
            Quantidade = quantidadeMantida;

            return devolvidas;
        }

        Status = StatusDoPedido.Cancelado;
        CanceladoEm = DateTime.UtcNow;

        return devolvidas;
    }
}
