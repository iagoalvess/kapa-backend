using Backend.Business.Auth.Interfaces;

namespace Backend.Worker.Jobs;

/// <summary>
/// Apaga refresh tokens expirados ou revogados há mais tempo que a retenção.
/// </summary>
/// <remarks>
/// É o job de referência do projeto — copie a forma dele. Três coisas que ele faz de propósito:
/// <list type="bullet">
/// <item>abre um escopo por execução, porque repositórios são <c>scoped</c> e um
/// <c>BackgroundService</c> é singleton: injetar o repositório direto no construtor prenderia
/// um <c>DbContext</c> vivo pelo tempo todo do processo;</item>
/// <item>roda uma vez ao subir e depois no intervalo, para não esperar seis horas até a
/// primeira execução em um container que reinicia;</item>
/// <item>engole a exceção e segue, porque falha em uma execução não pode derrubar o host —
/// o que mataria também os outros jobs.</item>
/// </list>
/// <para>
/// Usa <see cref="PeriodicTimer"/> do runtime em vez de Hangfire ou Quartz. O teto disso é
/// conhecido: sem persistência de agendamento, sem retry automático e sem painel. Quando o
/// projeto precisar de <b>uma</b> dessas três coisas, aí sim entra o Hangfire — e este job vira
/// um método com <c>RecurringJob.AddOrUpdate</c>, sem mudar nada em Business.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class LimpezaRefreshTokensJob(IServiceScopeFactory scopeFactory, ILogger<LimpezaRefreshTokensJob> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(6);

    private static readonly TimeSpan Retencao = TimeSpan.FromDays(30);

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
            var repositorio = escopo.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();

            var removidos = await repositorio.RemoverInativosAnterioresA(DateTime.UtcNow - Retencao, ct);

            if (removidos > 0)
                logger.LogInformation("Limpeza de refresh tokens removeu {Removidos} registros.", removidos);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            logger.LogError(excecao, "Falha na limpeza de refresh tokens. A próxima execução tentará de novo.");
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
