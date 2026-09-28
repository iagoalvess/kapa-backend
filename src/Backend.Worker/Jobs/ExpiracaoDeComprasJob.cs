using Backend.Business.Abstractions;
using Backend.Business.Loja.Interfaces;
using Backend.Business.Loja.Models;
using Backend.Business.Loja.Services;
using Backend.Business.Pagamentos.Services;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Backend.Worker.Configuration;

namespace Backend.Worker.Jobs;

/// <summary>
/// Devolve ao estoque o que a loja reservou e ninguém pagou (Sprint 26, decisões 3, 7 e 9).
/// </summary>
/// <remarks>
/// Antes de expirar, pergunta ao Mercado Pago (decisão 9): a compra paga cujo aviso se perdeu é confirmada
/// pela mesma <see cref="BaixaAutomatica.Conciliar"/> do aviso, e aí o <c>UPDATE</c> condicional da expiração
/// não a acha mais pendente. Webhook perdido não vira lugar revendido.
/// <para>
/// Sob a <see cref="LiderancaDeJob"/>, uma réplica por vez; e mesmo sem ela, expirar é condicional — rodar
/// duas vezes não devolve duas. A cada minuto: o PIX da loja vale 30 minutos, e um lugar preso um minuto a
/// mais é a pior consequência de um atraso.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Um escopo por turma.</param>
/// <param name="lideranca">Uma réplica por vez.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ExpiracaoDeComprasJob(IServiceScopeFactory scopeFactory, LiderancaDeJob lideranca, ILogger<ExpiracaoDeComprasJob> logger)
    : JobPeriodico(lideranca, logger)
{
    /// <summary>Teto por rodada.</summary>
    public const int PorRodada = 200;

    /// <inheritdoc />
    protected override TimeSpan Intervalo { get; } = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override void RegistrarFalha(Exception excecao) =>
        Logger.LogError(excecao, "Falha na expiração das compras da loja. A próxima execução tentará de novo.");

    /// <inheritdoc />
    protected override async Task ExecutarPassada(int passada, CancellationToken ct)
    {
        IReadOnlyList<CompraAExpirar> vencidas;

        using (var escopo = scopeFactory.CreateScope())
            vencidas = await escopo
                .ServiceProvider.GetRequiredService<ICompraDeConviteRepository>()
                .ListarAExpirarDeTodasAsFormaturas(DateTime.UtcNow, PorRodada, ct);

        foreach (var vencida in vencidas)
        {
            try
            {
                using var escopo = scopeFactory.CreateScope();
                var sp = escopo.ServiceProvider;
                sp.GetRequiredService<FormaturaDoProcessamento>().Apontar(vencida.FormaturaId);

                if (
                    await sp.GetRequiredService<IProvedorDaTurmaRepository>().ObterViva(CobrancaBancaria.ChaveDaCompra(vencida.CompraId), ct) is
                    { } cobranca
                )
                {
                    var conciliada = await sp.GetRequiredService<BaixaAutomatica>().Conciliar(cobranca.Id, ct);
                    if (conciliada.Falhou)
                    {
                        Logger.LogWarning(
                            "Compra {CompraId} não expirada: o Mercado Pago não respondeu ({Motivo}). Tenta na próxima.",
                            vencida.CompraId,
                            conciliada.PrimeiroErro.Mensagem
                        );
                        continue;
                    }
                }

                await sp.GetRequiredService<PagamentoDaCompra>().Expirar(vencida.CompraId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception excecao)
            {
                Logger.LogError(excecao, "Falha ao expirar a compra {CompraId}. As demais seguem.", vencida.CompraId);
            }
        }
    }
}
