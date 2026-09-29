using System.Text.Json.Serialization;

namespace Backend.Business.Formaturas.Models;

/// <summary>As vendas de pé da festa (Sprint 38, P10) — vão em <c>dados</c> no 409 <c>agenda.evento_com_vendas</c>.</summary>
/// <param name="ComprasDaLoja">Compras da loja pendentes ou com lugar valendo.</param>
/// <param name="PedidosDeConvite">Pedidos de convite de formando confirmados.</param>
public sealed record VendasDaFesta(int ComprasDaLoja, int PedidosDeConvite)
{
    /// <summary>Se há alguma. Fora do <c>dados</c> do erro: é conta, não contagem.</summary>
    [JsonIgnore]
    public bool Alguma => ComprasDaLoja > 0 || PedidosDeConvite > 0;
}

/// <summary>
/// O que impede encerrar a turma (Sprint 38, P11) — vai em <c>dados</c> no 409 <c>formatura.pendencias_em_aberto</c>.
/// </summary>
/// <param name="ParcelasEmAberto">Parcelas abertas ou vencidas.</param>
/// <param name="AvisosDePagamento">Avisos de pagamento esperando a tesouraria.</param>
/// <param name="PedidosNaoQuitados">Pedidos de formando confirmados com parcela em aberto.</param>
/// <param name="ComprasPendentes">Compras da loja esperando pagamento.</param>
/// <param name="ComprasADevolver">Compras da loja com dinheiro a devolver.</param>
/// <param name="PedidosDeCancelamento">Pedidos de cancelamento sem resposta.</param>
/// <param name="CobrancasVivas">Cobranças do Mercado Pago que ainda podem ser pagas.</param>
public sealed record PendenciasDaTurma(
    int ParcelasEmAberto,
    int AvisosDePagamento,
    int PedidosNaoQuitados,
    int ComprasPendentes,
    int ComprasADevolver,
    int PedidosDeCancelamento,
    int CobrancasVivas
)
{
    /// <summary>Se há alguma. Fora do <c>dados</c> do erro: é conta, não contagem.</summary>
    [JsonIgnore]
    public bool Alguma =>
        ParcelasEmAberto + AvisosDePagamento + PedidosNaoQuitados + ComprasPendentes + ComprasADevolver + PedidosDeCancelamento + CobrancasVivas > 0;

    /// <summary>O que falta, em frases curtas, na ordem em que a turma resolve.</summary>
    public IReadOnlyList<string> Descrever() =>
        [
            .. new (int Quantos, string Um, string Varios)[]
            {
                (ParcelasEmAberto, "1 parcela em aberto", "parcelas em aberto"),
                (AvisosDePagamento, "1 aviso de pagamento para conferir", "avisos de pagamento para conferir"),
                (PedidosNaoQuitados, "1 pedido de formando não quitado", "pedidos de formando não quitados"),
                (ComprasPendentes, "1 compra da loja aguardando pagamento", "compras da loja aguardando pagamento"),
                (ComprasADevolver, "1 compra da loja a devolver", "compras da loja a devolver"),
                (PedidosDeCancelamento, "1 pedido de cancelamento sem resposta", "pedidos de cancelamento sem resposta"),
                (CobrancasVivas, "1 cobrança do Mercado Pago em aberto", "cobranças do Mercado Pago em aberto"),
            }
                .Where(linha => linha.Quantos > 0)
                .Select(linha => linha.Quantos == 1 ? linha.Um : $"{linha.Quantos} {linha.Varios}"),
        ];
}
