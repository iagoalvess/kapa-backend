using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Emails.Services;

/// <summary>
/// Escreve a mensagem no log em vez de enviá-la.
/// </summary>
/// <remarks>
/// Registrado automaticamente quando <c>Smtp:Host</c> está vazio. Serve para desenvolvimento e
/// para os testes: o projeto sobe e o fluxo de e-mail funciona de ponta a ponta sem ninguém
/// precisar de um servidor SMTP à mão.
/// <para>
/// A aplicação avisa no boot quando está neste modo — um ambiente que deveria enviar e-mail e
/// está só logando precisa ser barulhento, não silencioso.
/// </para>
/// </remarks>
/// <param name="logger">Log estruturado.</param>
public sealed class EmailSenderDeLog(ILogger<EmailSenderDeLog> logger) : IEmailSender
{
    /// <inheritdoc />
    public Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct = default)
    {
        logger.LogInformation(
            "[SMTP não configurado] E-mail não enviado. Para: {Destinatario} | Assunto: {Assunto}",
            mensagem.Para,
            mensagem.Assunto
        );

        return Task.CompletedTask;
    }
}
