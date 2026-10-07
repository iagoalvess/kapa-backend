using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend.Business.Common.Texto;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Backend.Business.Emails.Services;

/// <summary>
/// Entrega mensagens pela API HTTP da MillionSend (<c>POST /emails</c>, o mesmo formato do Resend).
/// </summary>
/// <remarks>
/// HTTP, e não SMTP, porque a MillionSend Cloud não expõe o relay SMTP — ele só existe na versão
/// hospedada por conta própria (07/10/2026).
/// <para>
/// A mensagem é montada pelo <see cref="SmtpEmailSender.Montar"/>: remetente, desvio de desenvolvimento e
/// cabeçalhos do marketing seguem uma regra só, e aqui ela só muda de formato. A API recusa <c>content_id</c>,
/// então as imagens da marca saem por URL — por isso <c>Smtp:UrlDasImagens</c> é obrigatório com ela.
/// </para>
/// </remarks>
/// <param name="http">Cliente HTTP compartilhado, com conexões recicladas.</param>
/// <param name="montagem">Quem monta a mensagem, a mesma do SMTP.</param>
/// <param name="options">Configuração do envio.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class MillionSendEmailSender(
    HttpClient http,
    SmtpEmailSender montagem,
    IOptions<SmtpSettings> options,
    ILogger<MillionSendEmailSender> logger
) : IEmailSender
{
    private const string Endereco = "https://api.millionsend.com/emails";

    private readonly SmtpSettings _settings = options.Value;

    /// <inheritdoc />
    /// <remarks>
    /// A recusa sai como <see cref="HttpRequestException"/> com o status: a fila desiste na hora do 4xx, que é recusa da
    /// própria mensagem, e reagenda o resto.
    /// </remarks>
    public async Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct = default)
    {
        using var mime = montagem.Montar(mensagem);

        using var requisicao = new HttpRequestMessage(HttpMethod.Post, Endereco) { Content = JsonContent.Create(Corpo(mime)) };
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKeyDaMillionSend);

        using var resposta = await http.SendAsync(requisicao, ct);

        if (!resposta.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"MillionSend recusou o e-mail: {(int)resposta.StatusCode} {await resposta.Content.ReadAsStringAsync(ct)}",
                null,
                resposta.StatusCode
            );
        }

        logger.LogInformation("E-mail entregue à MillionSend para {Destinatario}.", TextoUtils.MascararEmail(mime.To.ToString()));
    }

    /// <summary>O JSON do <c>POST /emails</c>, tirado da mensagem MIME já montada.</summary>
    /// <param name="mime">A mensagem como o SMTP a mandaria.</param>
    public static Dictionary<string, object?> Corpo(MimeMessage mime)
    {
        var corpo = new Dictionary<string, object?>
        {
            ["from"] = mime.From.ToString(),
            ["to"] = mime.To.Mailboxes.Select(m => m.Address).ToArray(),
            ["subject"] = mime.Subject,
            ["html"] = mime.HtmlBody ?? string.Empty,
        };

        if (mime.ReplyTo.Count > 0)
            corpo["reply_to"] = mime.ReplyTo.Mailboxes.Select(m => m.Address).ToArray();

        if (mime.Headers.Contains("List-Unsubscribe"))
            corpo["headers"] = new Dictionary<string, string?>
            {
                ["List-Unsubscribe"] = mime.Headers["List-Unsubscribe"],
                ["List-Unsubscribe-Post"] = mime.Headers["List-Unsubscribe-Post"],
            };

        var anexos = mime.Attachments.OfType<MimePart>().Select(Anexo).ToArray();
        if (anexos.Length > 0)
            corpo["attachments"] = anexos;

        return corpo;
    }

    private static Dictionary<string, string?> Anexo(MimePart parte)
    {
        using var memoria = new MemoryStream();
        parte.Content!.DecodeTo(memoria);

        return new()
        {
            ["filename"] = parte.FileName,
            ["content"] = Convert.ToBase64String(memoria.ToArray()),
            ["content_type"] = parte.ContentType.MimeType,
        };
    }
}
