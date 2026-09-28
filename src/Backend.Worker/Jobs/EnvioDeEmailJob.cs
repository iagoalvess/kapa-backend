using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Settings;
using Backend.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace Backend.Worker.Jobs;

/// <summary>
/// Envia os e-mails pendentes da fila.
/// </summary>
/// <remarks>
/// A regra de reservar, enviar e registrar está em <see cref="IProcessamentoDaFilaDeEmail"/>; aqui
/// ficam o relógio, o escopo e o log.
/// <para>
/// <b>Sem <see cref="LiderancaDeJob"/></b>, e é a diferença para os demais jobs: a
/// reserva usa <c>FOR UPDATE SKIP LOCKED</c>, então várias réplicas dividem a fila sem mandar o mesmo
/// e-mail duas vezes.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="options">Configuração de envio.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class EnvioDeEmailJob(IServiceScopeFactory scopeFactory, IOptions<SmtpSettings> options, ILogger<EnvioDeEmailJob> logger)
    : JobPeriodico(null, logger)
{
    /// <summary>
    /// De quanto em quanto tempo a fila é varrida.
    /// </summary>
    /// <remarks>
    /// Dez segundos: é o teto do atraso de um código de verificação, que a pessoa espera com a tela
    /// aberta. A rodada vazia custa um <c>SELECT ... SKIP LOCKED</c> que não acha nada.
    /// </remarks>
    private static readonly TimeSpan IntervaloDaFila = TimeSpan.FromSeconds(10);

    private static readonly int PassadasEntreLimpezas = (int)(TimeSpan.FromHours(1) / IntervaloDaFila);

    /// <inheritdoc />
    protected override TimeSpan Intervalo => IntervaloDaFila;

    /// <inheritdoc />
    protected override void RegistrarFalha(Exception excecao) =>
        Logger.LogError(excecao, "Falha ao processar a fila de e-mails. A próxima rodada tentará de novo.");

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Configurado)
            Logger.LogWarning("Smtp:Host não configurado — os e-mails serão apenas registrados no log, não enviados.");

        return base.ExecuteAsync(stoppingToken);
    }

    /// <inheritdoc />
    /// <remarks>Lote cheio quer dizer que há mais esperando: roda outro na mesma passada.</remarks>
    protected override async Task ExecutarPassada(int passada, CancellationToken ct)
    {
        if (passada % PassadasEntreLimpezas == 0)
            await Limpar(ct);

        while (await ProcessarLote(ct)) { }
    }

    private async Task<bool> ProcessarLote(CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        return await escopo.ServiceProvider.GetRequiredService<IProcessamentoDaFilaDeEmail>().ProcessarLote(ct);
    }

    /// <summary>Apaga os concluídos antigos e desiste dos presos em envio.</summary>
    /// <remarks>Falha aqui não impede o envio da passada: tem o próprio <c>catch</c>.</remarks>
    private async Task Limpar(CancellationToken ct)
    {
        try
        {
            using var escopo = scopeFactory.CreateScope();

            var limpeza = await escopo.ServiceProvider.GetRequiredService<IProcessamentoDaFilaDeEmail>().Limpar(ct);

            if (limpeza.Presos + limpeza.Removidos > 0)
                Logger.LogInformation(
                    "Limpeza da fila de e-mails: {Presos} presos dados por falhos, {Removidos} antigos apagados.",
                    limpeza.Presos,
                    limpeza.Removidos
                );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            Logger.LogError(excecao, "Falha na limpeza da fila de e-mails. A próxima execução tentará de novo.");
        }
    }
}
