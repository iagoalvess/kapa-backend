using Backend.Business.Eventos.Interfaces;

namespace Backend.Worker.Jobs;

/// <summary>
/// Apaga eventos mais antigos que o prazo de retenção.
/// </summary>
/// <remarks>
/// A tabela de eventos registra atividade, não estado: ela só cresce, e cresce proporcional ao
/// uso. Sem retenção, é a primeira a encher o disco — e a que mais lentifica as consultas do
/// painel administrativo no caminho.
/// <para>
/// O prazo vem de <c>Eventos:DiasDeRetencao</c>. Antes de encurtá-lo, confira se alguém depende
/// de comparação ano a ano: 180 dias não respondem "cresceu em relação ao mesmo mês do ano
/// passado".
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="configuration">Configuração da aplicação.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class RetencaoDeEventosJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<RetencaoDeEventosJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(24);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var diasDeRetencao = configuration.GetValue("Eventos:DiasDeRetencao", 180);

        using var relogio = new PeriodicTimer(Intervalo);

        do
        {
            await ExecutarUmaVez(diasDeRetencao, stoppingToken);
        } while (await EsperarProximaExecucao(relogio, stoppingToken));
    }

    private async Task ExecutarUmaVez(int diasDeRetencao, CancellationToken ct)
    {
        try
        {
            using var escopo = scopeFactory.CreateScope();
            var repositorio = escopo.ServiceProvider.GetRequiredService<IEventoRepository>();

            var removidos = await repositorio.RemoverAnterioresA(DateTime.UtcNow.AddDays(-diasDeRetencao), ct);

            if (removidos > 0)
                logger.LogInformation("Retenção removeu {Removidos} eventos anteriores a {Dias} dias.", removidos, diasDeRetencao);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            logger.LogError(excecao, "Falha na retenção de eventos. A próxima execução tentará de novo.");
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
