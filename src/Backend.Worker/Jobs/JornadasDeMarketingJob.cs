using Backend.Business.Marketing.Interfaces;
using Backend.Worker.Configuration;

namespace Backend.Worker.Jobs;

/// <summary>
/// De hora em hora: as jornadas de marketing do Kapa com a comissão das turmas do gratuito (Sprint 40).
/// </summary>
/// <remarks>
/// Mesma forma dos demais jobs — escopo por execução, roda ao subir, engole a exceção e segue. A regra está em
/// <see cref="IJornadasDeMarketingService"/>, inclusive o interruptor (desligado até a P9) e a janela de 9h às 20h.
/// <para>
/// Um escopo só, sem turma apontada: a jornada é da pessoa e atravessa turmas. De hora em hora pelo mesmo motivo
/// da régua — uma rodada perdida num reinício não custa o dia —, e rodar de novo não duplica: o índice único do
/// envio e a trava dos 14 dias seguram.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos.</param>
/// <param name="lideranca">Uma réplica por vez.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class JornadasDeMarketingJob(IServiceScopeFactory scopeFactory, LiderancaDeJob lideranca, ILogger<JornadasDeMarketingJob> logger)
    : JobPeriodico(lideranca, logger)
{
    /// <inheritdoc />
    protected override TimeSpan Intervalo { get; } = TimeSpan.FromHours(1);

    /// <inheritdoc />
    protected override void RegistrarFalha(Exception excecao) =>
        Logger.LogError(excecao, "Falha nas jornadas de marketing. A próxima execução tentará de novo.");

    /// <inheritdoc />
    protected override async Task ExecutarPassada(int passada, CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        var enfileirados = await escopo.ServiceProvider.GetRequiredService<IJornadasDeMarketingService>().Executar(DateTime.UtcNow, ct);

        if (enfileirados > 0)
            Logger.LogInformation("Jornadas de marketing: {Enfileirados} e-mails na fila.", enfileirados);
    }
}
