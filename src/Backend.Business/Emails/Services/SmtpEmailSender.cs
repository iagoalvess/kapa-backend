using Backend.Business.Common.Texto;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Backend.Business.Emails.Services;

/// <summary>
/// Entrega mensagens por SMTP, usando MailKit.
/// </summary>
/// <remarks>
/// MailKit e não <c>System.Net.Mail.SmtpClient</c>: a própria Microsoft marca o
/// <c>SmtpClient</c> como obsoleto para código novo — ele não suporta as formas modernas de
/// autenticação e tem comportamento problemático de conexão.
/// <para>
/// A conexão é aberta e fechada por mensagem. É mais lento que manter sessão, e é o certo aqui:
/// o envio roda em lote no worker, fora do caminho da requisição, e conexão SMTP ociosa é
/// derrubada pelo servidor sem aviso — reaproveitá-la troca lentidão por falha intermitente.
/// </para>
/// </remarks>
/// <param name="options">Configuração do servidor.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class SmtpEmailSender(IOptions<SmtpSettings> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpSettings _settings = options.Value;

    /// <inheritdoc />
    /// <remarks>
    /// A porta escolhe o modo de TLS: 465 é SSL desde o handshake, as demais começam em texto
    /// puro e sobem com STARTTLS. Combinar a opção errada com a porta não dá erro imediato —
    /// a conexão trava até o timeout, que é bem mais difícil de diagnosticar.
    /// </remarks>
    public async Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct = default)
    {
        using var mime = new MimeMessage { Subject = mensagem.Assunto, Body = new BodyBuilder { HtmlBody = mensagem.CorpoHtml }.ToMessageBody() };

        mime.From.Add(new MailboxAddress(_settings.RemetenteNome, _settings.RemetenteEmail));
        mime.To.Add(MailboxAddress.Parse(mensagem.Para));

        using var cliente = new SmtpClient();

        var seguranca = _settings.Porta == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

        await cliente.ConnectAsync(_settings.Host, _settings.Porta, seguranca, ct);

        if (!string.IsNullOrWhiteSpace(_settings.Usuario))
            await cliente.AuthenticateAsync(_settings.Usuario, _settings.Senha, ct);

        await cliente.SendAsync(mime, ct);
        await cliente.DisconnectAsync(true, ct);

        logger.LogInformation("E-mail entregue ao servidor SMTP para {Destinatario}.", TextoUtils.MascararEmail(mensagem.Para));
    }
}
