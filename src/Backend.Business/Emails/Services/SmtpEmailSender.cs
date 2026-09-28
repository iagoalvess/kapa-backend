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
    /// <para>
    /// Com <c>UrlDasImagens</c>, o logo e o mascote saem por URL pública (<see cref="ModeloDeEmail.ComImagensPorUrl"/>);
    /// sem ela — desenvolvimento, onde só existe <c>localhost</c> —, vão como <c>LinkedResource</c>, e quem
    /// diz quais são é <see cref="ModeloDeEmail.ImagensDe"/>.
    /// </para>
    /// <para>
    /// O destinatário original vai no assunto do desvio: sem ele, a caixa de quem desenvolve vira uma
    /// pilha de mensagens idênticas sem dizer de quem era cada uma.
    /// </para>
    /// </remarks>
    public async Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct = default)
    {
        var desviado = !string.IsNullOrWhiteSpace(_settings.RedirecionarPara);
        var destinatario = desviado ? _settings.RedirecionarPara : mensagem.Para;

        var porUrl = !string.IsNullOrWhiteSpace(_settings.UrlDasImagens);

        var corpo = new BodyBuilder
        {
            HtmlBody = porUrl ? ModeloDeEmail.ComImagensPorUrl(mensagem.CorpoHtml, _settings.UrlDasImagens) : mensagem.CorpoHtml,
        };

        if (!porUrl)
            foreach (var (cid, conteudo) in ModeloDeEmail.ImagensDe(mensagem.CorpoHtml))
                corpo.LinkedResources.Add(Inline(cid, conteudo));

        if (mensagem.Anexo is { } anexo)
            corpo.Attachments.Add(anexo.Nome, anexo.Conteudo, ContentType.Parse(anexo.ContentType));

        using var mime = new MimeMessage
        {
            Subject = desviado ? $"[para {mensagem.Para}] {mensagem.Assunto}" : mensagem.Assunto,
            Body = corpo.ToMessageBody(),
        };

        mime.From.Add(new MailboxAddress(_settings.RemetenteNome, _settings.RemetenteEmail));
        mime.To.Add(MailboxAddress.Parse(destinatario));

        using var cliente = new SmtpClient();

        var seguranca = _settings.Porta == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

        await cliente.ConnectAsync(_settings.Host, _settings.Porta, seguranca, ct);

        if (!string.IsNullOrWhiteSpace(_settings.Usuario))
            await cliente.AuthenticateAsync(_settings.Usuario, _settings.Senha, ct);

        await cliente.SendAsync(mime, ct);
        await cliente.DisconnectAsync(true, ct);

        if (desviado)
            logger.LogWarning(
                "E-mail de {Destinatario} desviado para {Caixa}: Smtp:RedirecionarPara está preenchido.",
                TextoUtils.MascararEmail(mensagem.Para),
                TextoUtils.MascararEmail(destinatario)
            );
        else
            logger.LogInformation("E-mail entregue ao servidor SMTP para {Destinatario}.", TextoUtils.MascararEmail(mensagem.Para));
    }

    /// <summary>
    /// Uma imagem do corpo, montada à mão para o cliente <b>não</b> a listar como anexo.
    /// </summary>
    /// <remarks>
    /// Duas coisas fazem a diferença, e as duas o <c>Add(nome, stream)</c> do MailKit faz ao
    /// contrário: disposição <c>inline</c> e <b>sem nome de arquivo</b>. Com nome, o Gmail entende
    /// que há algo para baixar e pendura o clipe na mensagem — o logo da marca virava um anexo
    /// chamado <c>kapa-logo.png</c> em toda cobrança.
    /// <para>
    /// O conteúdo é copiado para memória porque o corpo só é serializado no <c>SendAsync</c>, e o
    /// recurso embutido já sai do assembly inteiro na memória de qualquer forma.
    /// </para>
    /// </remarks>
    /// <param name="cid">O <c>Content-ID</c> que o HTML cita.</param>
    /// <param name="conteudo">O PNG.</param>
    private static MimePart Inline(string cid, Stream conteudo)
    {
        using (conteudo)
        {
            var memoria = new MemoryStream();
            conteudo.CopyTo(memoria);
            memoria.Position = 0;

            return new MimePart("image", "png")
            {
                Content = new MimeContent(memoria),
                ContentId = cid,
                ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
                ContentTransferEncoding = ContentEncoding.Base64,
            };
        }
    }
}
