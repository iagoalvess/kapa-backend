using Backend.Business.Abstractions;
using Backend.Business.Common.Texto;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Settings;
using Microsoft.Extensions.Options;

namespace Backend.Worker.Jobs;

/// <summary>
/// Envia os e-mails pendentes da fila.
/// </summary>
/// <remarks>
/// O ciclo tem três passos separados de propósito:
/// <list type="number">
/// <item><b>Reservar</b> — numa transação curta, marca o lote como "enviando". Sai com
/// commit antes de qualquer acesso à rede.</item>
/// <item><b>Enviar</b> — fora de transação. Manter o banco travado durante o SMTP seguraria
/// os locks pelo tempo da rede e bloquearia as outras réplicas.</item>
/// <item><b>Registrar o resultado</b> — numa segunda transação, marca enviado ou reagenda.</item>
/// </list>
/// <para>
/// A consequência de reservar antes de enviar: se o processo morrer entre o envio e o registro,
/// o e-mail fica preso em <c>Enviando</c>. É a escolha consciente entre "pode ficar preso" e
/// "pode ser enviado duas vezes" — e receber a mesma cobrança duas vezes é pior que não receber
/// e alguém reprocessar.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="emailSender">Entrega ao servidor de e-mail.</param>
/// <param name="options">Configuração de envio.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class EnvioDeEmailJob(
    IServiceScopeFactory scopeFactory,
    IEmailSender emailSender,
    IOptions<SmtpSettings> options,
    ILogger<EnvioDeEmailJob> logger
) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(30);

    private readonly SmtpSettings _settings = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Configurado)
            logger.LogWarning("Smtp:Host não configurado — os e-mails serão apenas registrados no log, não enviados.");

        using var relogio = new PeriodicTimer(Intervalo);

        do
        {
            await ProcessarLote(stoppingToken);
        } while (await EsperarProximaExecucao(relogio, stoppingToken));
    }

    private async Task ProcessarLote(CancellationToken ct)
    {
        try
        {
            using var escopo = scopeFactory.CreateScope();
            var repositorio = escopo.ServiceProvider.GetRequiredService<IEmailFilaRepository>();
            var unitOfWork = escopo.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var agora = DateTime.UtcNow;
            var lote = await unitOfWork.EmTransacaoAsync(token => repositorio.ReservarLote(_settings.TamanhoDoLote, agora, token), ct);

            if (lote.Count == 0)
                return;

            foreach (var email in lote)
                await EnviarUm(email, ct);

            await unitOfWork.SalvarAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            logger.LogError(excecao, "Falha ao processar a fila de e-mails. A próxima rodada tentará de novo.");
        }
    }

    private async Task EnviarUm(EmailNaFila email, CancellationToken ct)
    {
        try
        {
            await emailSender.EnviarAsync(new MensagemDeEmail(email.Para, email.Assunto, email.CorpoHtml), ct);
            email.MarcarEnviado(DateTime.UtcNow);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            email.RegistrarFalha(excecao.Message, DateTime.UtcNow, _settings.MaximoDeTentativas);

            logger.LogWarning(
                excecao,
                "Falha ao enviar e-mail {EmailId} para {Destinatario} (tentativa {Tentativa} de {Maximo}). Status: {Status}.",
                email.Id,
                TextoUtils.MascararEmail(email.Para),
                email.Tentativas,
                _settings.MaximoDeTentativas,
                email.Status
            );
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
