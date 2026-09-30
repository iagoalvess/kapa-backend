using Backend.Business.Abstractions;
using Backend.Business.Pagamentos.Services;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Loja.Services;

/// <summary>
/// Devolve ao estoque a compra que venceu sem pagamento — depois de perguntar ao Mercado Pago (Sprint 26,
/// decisões 7 e 9).
/// </summary>
/// <remarks>
/// A compra paga cujo aviso se perdeu é confirmada pela mesma <see cref="BaixaAutomatica.Conciliar"/> do aviso,
/// e aí o <c>UPDATE</c> condicional da expiração não a acha mais pendente. Webhook perdido não vira lugar revendido.
/// <para>
/// Classe própria, e não um método de <see cref="PagamentoDaCompra"/> ou do <c>CancelamentoDaCompra</c>: a
/// <see cref="BaixaAutomatica"/> depende dos dois, e depender dela ali fecharia um ciclo na injeção. Sem
/// interface, como eles: uma implementação, e ninguém de fora a substitui.
/// </para>
/// </remarks>
/// <param name="provedor">A cobrança viva da compra, se houver.</param>
/// <param name="baixa">A consulta ao Mercado Pago e a baixa.</param>
/// <param name="pagamento">A expiração condicional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ExpiracaoDaCompra(
    IProvedorDaTurmaRepository provedor,
    BaixaAutomatica baixa,
    PagamentoDaCompra pagamento,
    ILogger<ExpiracaoDaCompra> logger
)
{
    /// <summary>
    /// Concilia a cobrança viva da compra e, se ela não estava paga, expira a compra e devolve o estoque.
    /// </summary>
    /// <remarks>
    /// Mercado Pago fora do ar na consulta devolve a falha e não expira: a próxima rodada tenta de novo. Na formatura
    /// que quem chama apontou no escopo.
    /// </remarks>
    /// <param name="compraId">A compra vencida.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Se expirou agora.</returns>
    public async Task<Result<bool>> Expirar(Guid compraId, CancellationToken ct = default)
    {
        if (await provedor.ObterViva(CobrancaBancaria.ChaveDaCompra(compraId), ct) is { } cobranca)
        {
            var conciliada = await baixa.Conciliar(cobranca.Id, ct);
            if (conciliada.Falhou)
            {
                logger.LogWarning(
                    "Compra {CompraId} não expirada: o Mercado Pago não respondeu ({Motivo}). Tenta na próxima.",
                    compraId,
                    conciliada.PrimeiroErro.Mensagem
                );
                return Result.Falha<bool>(conciliada.Erros);
            }
        }

        return await pagamento.Expirar(compraId, ct);
    }
}
