using Backend.Business.Abstractions;
using Backend.Business.Common.Texto;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business.Emails.Services;

/// <summary>
/// Envia os e-mails pendentes da fila e faz a faxina dela.
/// </summary>
/// <remarks>
/// O ciclo de um lote tem três passos separados de propósito:
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
/// <para>
/// É o único service que conhece <see cref="IEmailSender"/>, e quem o chama é o <c>EnvioDeEmailJob</c>
/// — o job fica com o relógio, o escopo e o log da passada.
/// </para>
/// </remarks>
/// <param name="repositorio">Acesso à fila.</param>
/// <param name="unitOfWork">Transação da reserva e do registro.</param>
/// <param name="emailSender">Entrega ao servidor de e-mail.</param>
/// <param name="options">Configuração de envio.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ProcessamentoDaFilaDeEmail(
    IEmailFilaRepository repositorio,
    IUnitOfWork unitOfWork,
    IEmailSender emailSender,
    IOptions<SmtpSettings> options,
    ILogger<ProcessamentoDaFilaDeEmail> logger
) : IProcessamentoDaFilaDeEmail
{
    /// <summary>Conexões SMTP ao mesmo tempo: cada envio abre a sua, e um por vez limitava a fila a ~2 por segundo.</summary>
    private const int EnviosEmParalelo = 4;

    /// <summary>Quanto tempo o e-mail enviado ou desistido fica na fila antes de ser apagado.</summary>
    private static readonly TimeSpan Retencao = TimeSpan.FromDays(30);

    /// <summary>Reservado há mais que isto e ainda em envio: o worker que o pegou morreu.</summary>
    private static readonly TimeSpan PresoApos = TimeSpan.FromMinutes(15);

    private readonly SmtpSettings _settings = options.Value;

    /// <inheritdoc />
    public async Task<bool> ProcessarLote(CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var lote = await unitOfWork.EmTransacaoAsync(token => repositorio.ReservarLote(_settings.TamanhoDoLote, agora, token), ct);

        if (lote.Count == 0)
            return false;

        await Parallel.ForEachAsync(
            lote,
            new ParallelOptions { MaxDegreeOfParallelism = EnviosEmParalelo, CancellationToken = ct },
            async (email, token) => await EnviarUm(email, token)
        );

        await unitOfWork.SalvarAsync(ct);

        return lote.Count == _settings.TamanhoDoLote;
    }

    /// <inheritdoc />
    public async Task<LimpezaDaFilaDeEmail> Limpar(CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        var presos = await repositorio.DesistirDosPresosAnterioresA(agora - PresoApos, ct);
        var removidos = await repositorio.RemoverConcluidosAnterioresA(agora - Retencao, ct);

        return new LimpezaDaFilaDeEmail(presos, removidos);
    }

    private async Task EnviarUm(EmailNaFila email, CancellationToken ct)
    {
        try
        {
            await emailSender.EnviarAsync(new MensagemDeEmail(email.Para, email.Assunto, email.CorpoHtml, email.Anexo), ct);
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
}
