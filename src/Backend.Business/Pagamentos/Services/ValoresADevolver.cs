using Backend.Business.Cobrancas.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;

namespace Backend.Business.Pagamentos.Services;

/// <summary>
/// Onde nasce e morre sozinho o que vai para a lista "a devolver" (Sprint 42): um lugar só, para todo cancelamento
/// de parcela chegar à mesma regra.
/// </summary>
/// <remarks>
/// Não chama <c>SalvarAsync</c>: o valor entra na transação de quem cancelou, baixou ou estornou — o cancelamento e o
/// que ele deixou a devolver ficam os dois, ou nenhum. Quem tira da lista pela mão da comissão é o
/// <see cref="ValoresADevolverService"/>.
/// <para>Sem interface, como <see cref="BaixaService"/>: uma implementação, e ninguém de fora a substitui.</para>
/// </remarks>
/// <param name="repositorio">Valores a devolver.</param>
public sealed class ValoresADevolver(IValorADevolverRepository repositorio)
{
    /// <summary>
    /// O que já tinha entrado nas parcelas que acabaram de ser canceladas vira "a devolver" (decisão 3).
    /// </summary>
    /// <remarks>
    /// Uma linha por parcela, e não uma soma: a devolução do Mercado Pago chega por parcela, e é dela que se abate.
    /// Quem chama passa só as canceladas agora — a que já estava cancelada já teve o seu registro.
    /// </remarks>
    /// <param name="canceladas">Parcelas canceladas nesta operação, rastreadas.</param>
    /// <returns>Quanto foi para a lista, em centavos.</returns>
    public async Task<long> RegistrarParciais(IEnumerable<Parcela> canceladas, CancellationToken ct = default)
    {
        var total = 0L;

        foreach (var parcela in canceladas.Where(p => p is { Status: StatusDaParcela.Cancelada, ValorPagoEmCentavos: > 0 }))
        {
            await repositorio.Adicionar(ValorADevolver.DaParcela(parcela), ct);
            total += parcela.ValorPagoEmCentavos!.Value;
        }

        return total;
    }

    /// <summary>O crédito do pedido cancelado pela tesouraria (decisão 2) — no lugar da parcela negativa de antes.</summary>
    /// <param name="pedido">Pedido cancelado.</param>
    /// <param name="creditoEmCentavos">Crédito, já limitado ao que o formando pagou nele.</param>
    public Task RegistrarCredito(Pedido pedido, long creditoEmCentavos, CancellationToken ct = default) =>
        repositorio.Adicionar(ValorADevolver.DoCredito(pedido, creditoEmCentavos), ct);

    /// <summary>O pagamento do Mercado Pago que não achou parcela aberta (decisão 9).</summary>
    /// <param name="parcela">A parcela que ele ia baixar.</param>
    /// <param name="cobrancaId">Cobrança.</param>
    /// <param name="valorEmCentavos">O que não baixou nada.</param>
    public Task RegistrarPagoSemParcela(Parcela parcela, Guid cobrancaId, long valorEmCentavos, CancellationToken ct = default) =>
        repositorio.Adicionar(ValorADevolver.DoPagoSemParcela(parcela, cobrancaId, valorEmCentavos), ct);

    /// <summary>
    /// A baixa de uma parcela cancelada foi estornada: o dinheiro voltou ao formando por outro caminho, e sai da lista.
    /// </summary>
    /// <remarks>
    /// Parcela que não está cancelada não tem nada aqui: o estorno dela a reabre, e o formando volta a dever.
    /// </remarks>
    /// <param name="parcela">A parcela, já estornada.</param>
    /// <param name="valorEmCentavos">O valor do estorno.</param>
    /// <param name="motivo">Por quê, para a observação.</param>
    public async Task AposEstorno(Parcela parcela, long valorEmCentavos, string motivo, CancellationToken ct = default)
    {
        if (parcela.Status != StatusDaParcela.Cancelada)
            return;

        var restante = valorEmCentavos;
        var agora = DateTime.UtcNow;

        foreach (var valor in await repositorio.ListarAbertosDaParcelaParaEdicao(parcela.Id, ct))
        {
            var abatido = Math.Min(restante, valor.ValorEmCentavos);
            valor.Abater(abatido, $"Estornado: {motivo}", agora);
            restante -= abatido;
        }
    }

    /// <summary>
    /// O Mercado Pago devolveu a cobrança que tinha pago sem parcela: o aviso da tesouraria se fecha sozinho — o que o
    /// webhook traz, o Kapa aplica (decisão 5).
    /// </summary>
    /// <param name="cobrancaId">Cobrança devolvida.</param>
    /// <param name="motivo">Devolução no painel ou contestação.</param>
    public async Task FecharDaCobranca(Guid cobrancaId, string motivo, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        foreach (var valor in await repositorio.ListarAbertosDaCobrancaParaEdicao(cobrancaId, ct))
            valor.Fechar(null, $"Mercado Pago: {motivo}", agora);
    }
}
