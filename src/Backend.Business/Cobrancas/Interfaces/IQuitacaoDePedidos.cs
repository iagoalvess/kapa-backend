using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// O que acontece com um pedido quando o dinheiro de uma parcela dele entra ou sai.
/// </summary>
/// <remarks>
/// A baixa e o estorno chamam isto e não sabem o que existe do outro lado (Sprint 21, P2): o gatilho
/// do convite fica no pedido. Roda dentro da transação da baixa, depois de a parcela ter sido marcada;
/// não salva nada sozinho. Parcela que não é de pedido passa direto.
/// </remarks>
public interface IQuitacaoDePedidos
{
    /// <summary>Se a parcela quitou o pedido de convite extra, os convites nascem — uma vez só.</summary>
    /// <param name="parcela">Parcela recém-paga, ainda rastreada.</param>
    Task AposBaixa(Parcela parcela, CancellationToken ct = default);

    /// <summary>O pagamento foi desfeito: os convites do pedido morrem com ele (decisão 8).</summary>
    /// <param name="parcela">Parcela estornada.</param>
    Task AposEstorno(Parcela parcela, CancellationToken ct = default);
}
