using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Emails.Interfaces;
using Backend.Worker.Configuration;
using Backend.Worker.Metricas;

namespace Backend.Worker.Jobs;

/// <summary>
/// Mede a profundidade das filas do banco e o silêncio do webhook, a cada minuto.
/// </summary>
/// <remarks>
/// <b>Sem <see cref="LiderancaDeJob"/></b>, como o <c>EnvioDeEmailJob</c>: é leitura pura, e cada
/// réplica publica o próprio ponto sem risco de efeito duplicado. Rodar em todas também evita o
/// painel ficar cego se a réplica que segura a liderança cair.
/// <para>
/// O <see cref="MetricasDeFilas"/> é singleton e vive além da passada; o job só o atualiza.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos, porque os repositórios são scoped.</param>
/// <param name="metricas">Medidor compartilhado do processo.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class MetricasDeFilasJob(IServiceScopeFactory scopeFactory, MetricasDeFilas metricas, ILogger<MetricasDeFilasJob> logger)
    : JobPeriodico(null, logger)
{
    /// <summary>Um e-mail em envio desde antes disto é preso — o worker que o pegou morreu.</summary>
    private static readonly TimeSpan IdadeDoPreso = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    protected override TimeSpan Intervalo { get; } = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override void RegistrarFalha(Exception excecao) =>
        Logger.LogError(excecao, "Falha ao medir as filas. A próxima execução tentará de novo.");

    /// <inheritdoc />
    protected override async Task ExecutarPassada(int passada, CancellationToken ct)
    {
        var agoraUtc = DateTime.UtcNow;

        using var escopo = scopeFactory.CreateScope();

        var emails = await escopo.ServiceProvider.GetRequiredService<IEmailFilaRepository>().ContarProfundidade(agoraUtc - IdadeDoPreso, ct);
        var ultimoWebhook = await escopo.ServiceProvider.GetRequiredService<IAssinaturaRepository>().UltimoEventoRecebidoDeTodasAsFormaturas(ct);

        metricas.AtualizarEmail(emails, agoraUtc);
        metricas.AtualizarWebhook(ultimoWebhook, agoraUtc);
    }
}
