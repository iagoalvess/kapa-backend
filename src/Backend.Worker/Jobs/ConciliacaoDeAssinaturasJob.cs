using Backend.Business.Assinaturas.Interfaces;
using Backend.Worker.Configuration;

namespace Backend.Worker.Jobs;

/// <summary>
/// De hora em hora: acha pagamento cujo webhook se perdeu, vence assinatura sem renovação e manda os
/// avisos de vencimento.
/// </summary>
/// <remarks>
/// Mesma forma dos demais jobs (<see cref="JobPeriodico"/>): escopo por execução, roda ao subir, engole a
/// exceção e segue. A regra toda está em <see cref="IWebhookService.Conciliar"/> — o job só marca a
/// hora.
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="lideranca">Trava que deixa só uma réplica rodar este job por vez.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ConciliacaoDeAssinaturasJob(
    IServiceScopeFactory scopeFactory,
    LiderancaDeJob lideranca,
    ILogger<ConciliacaoDeAssinaturasJob> logger
) : JobPeriodico(lideranca, logger)
{
    /// <inheritdoc />
    protected override TimeSpan Intervalo { get; } = TimeSpan.FromHours(1);

    /// <inheritdoc />
    protected override void RegistrarFalha(Exception excecao) =>
        Logger.LogError(excecao, "Falha na conciliação de assinaturas. A próxima execução tentará de novo.");

    /// <inheritdoc />
    protected override async Task ExecutarPassada(int passada, CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();
        var webhookService = escopo.ServiceProvider.GetRequiredService<IWebhookService>();

        var resumo = (await webhookService.Conciliar(DateTime.UtcNow, ct)).Valor;

        if (resumo is not { Confirmadas: 0, Renovadas: 0, Vencidas: 0, Avisos: 0 })
            Logger.LogInformation(
                "Conciliação de assinaturas: {Confirmadas} confirmadas, {Renovadas} renovadas, {Vencidas} vencidas, {Avisos} avisos.",
                resumo.Confirmadas,
                resumo.Renovadas,
                resumo.Vencidas,
                resumo.Avisos
            );
    }
}
