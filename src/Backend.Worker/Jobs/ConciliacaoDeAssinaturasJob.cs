using Backend.Business.Assinaturas.Interfaces;

namespace Backend.Worker.Jobs;

/// <summary>
/// De hora em hora: acha pagamento cujo webhook se perdeu, vence assinatura sem renovação e manda os
/// avisos de vencimento.
/// </summary>
/// <remarks>
/// Mesma forma do <see cref="LimpezaRefreshTokensJob"/>: escopo por execução, roda ao subir, engole a
/// exceção e segue. A regra toda está em <see cref="IWebhookService.Conciliar"/> — o job só marca a
/// hora.
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ConciliacaoDeAssinaturasJob(IServiceScopeFactory scopeFactory, ILogger<ConciliacaoDeAssinaturasJob> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var relogio = new PeriodicTimer(Intervalo);

        do
        {
            await ExecutarUmaVez(stoppingToken);
        } while (await EsperarProximaExecucao(relogio, stoppingToken));
    }

    private async Task ExecutarUmaVez(CancellationToken ct)
    {
        try
        {
            using var escopo = scopeFactory.CreateScope();
            var webhookService = escopo.ServiceProvider.GetRequiredService<IWebhookService>();

            var resumo = (await webhookService.Conciliar(DateTime.UtcNow, ct)).Valor;

            if (resumo is not { Confirmadas: 0, Vencidas: 0, Avisos: 0 })
                logger.LogInformation(
                    "Conciliação de assinaturas: {Confirmadas} confirmadas, {Vencidas} vencidas, {Avisos} avisos.",
                    resumo.Confirmadas,
                    resumo.Vencidas,
                    resumo.Avisos
                );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            logger.LogError(excecao, "Falha na conciliação de assinaturas. A próxima execução tentará de novo.");
        }
    }

    private static async Task<bool> EsperarProximaExecucao(PeriodicTimer relogio, CancellationToken ct)
    {
        try
        {
            return await relogio.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
