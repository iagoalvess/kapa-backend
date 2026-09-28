using Backend.Business.Auth.Interfaces;
using Backend.Worker.Configuration;

namespace Backend.Worker.Jobs;

/// <summary>
/// Apaga refresh tokens expirados ou revogados há mais tempo que a retenção.
/// </summary>
/// <remarks>
/// É o job de referência do projeto — copie a forma dele. Três coisas que ele faz de propósito, as
/// duas últimas herdadas de <see cref="JobPeriodico"/>:
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
/// <param name="lideranca">Trava que deixa só uma réplica rodar este job por vez.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class LimpezaRefreshTokensJob(IServiceScopeFactory scopeFactory, LiderancaDeJob lideranca, ILogger<LimpezaRefreshTokensJob> logger)
    : JobPeriodico(lideranca, logger)
{
    private static readonly TimeSpan Retencao = TimeSpan.FromDays(30);

    /// <inheritdoc />
    protected override TimeSpan Intervalo { get; } = TimeSpan.FromHours(6);

    /// <inheritdoc />
    protected override void RegistrarFalha(Exception excecao) =>
        Logger.LogError(excecao, "Falha na limpeza de refresh tokens. A próxima execução tentará de novo.");

    /// <inheritdoc />
    protected override async Task ExecutarPassada(int passada, CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();

        var removidos = await repositorio.RemoverInativosAnterioresA(DateTime.UtcNow - Retencao, ct);

        if (removidos > 0)
            Logger.LogInformation("Limpeza de refresh tokens removeu {Removidos} registros.", removidos);
    }
}
