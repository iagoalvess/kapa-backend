using Backend.Business.Eventos.Interfaces;

namespace Backend.Api.Analytics;

/// <summary>
/// Drena a fila de eventos e grava em lote.
/// </summary>
/// <remarks>
/// Roda dentro do processo da API, e não no worker, porque a fila é memória deste processo.
/// <para>
/// Falha de gravação descarta o lote e segue. É a escolha certa para este canal: insistir
/// acumularia memória enquanto o banco está em dificuldade, que é exatamente a hora de não
/// piorar. O erro vai para o log.
/// </para>
/// </remarks>
/// <param name="fila">Fila em memória.</param>
/// <param name="scopeFactory">Fábrica de escopos, para obter o repositório.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class FlushDeEventosService(FilaDeEventos fila, IServiceScopeFactory scopeFactory, ILogger<FlushDeEventosService> logger)
    : BackgroundService
{
    private const int TamanhoDoLote = 500;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var lote = await fila.LerLoteAsync(TamanhoDoLote, stoppingToken);

                if (lote.Count == 0)
                    continue;

                using var escopo = scopeFactory.CreateScope();
                await escopo.ServiceProvider.GetRequiredService<IEventoRepository>().GravarLote(lote, stoppingToken);

                ReportarDescartes();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception excecao)
            {
                logger.LogError(excecao, "Falha ao gravar lote de eventos. O lote foi descartado.");
            }
        }
    }

    private void ReportarDescartes()
    {
        var descartados = fila.DescartadosEZerar();

        if (descartados > 0)
            logger.LogWarning("{Descartados} eventos descartados por fila cheia. A gravação não está acompanhando a produção.", descartados);
    }
}
