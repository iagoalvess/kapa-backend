using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Emails.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Backend.UnitTests.Emails;

/// <summary>O JSON do <c>POST /emails</c> sai da mesma mensagem que o SMTP mandaria.</summary>
public sealed class MillionSendEmailSenderTests
{
    private static readonly SmtpEmailSender Montagem = new(
        Options.Create(
            new SmtpSettings
            {
                RemetenteEmail = "avisos@kapaformaturas.com.br",
                RemetenteNome = "Kapa",
                ResponderMarketingPara = "suporte@kapaformaturas.com.br",
                UrlDasImagens = "https://api.kapa/marca",
            }
        ),
        NullLogger<SmtpEmailSender>.Instance
    );

    [Fact]
    public void Marketing_leva_remetente_resposta_cabecalhos_e_anexo()
    {
        // Arrange
        var mensagem = new MensagemDeEmail(
            "ana@exemplo.com",
            "Assunto",
            "<p>Oi</p>",
            new AnexoDoEmail("convite.pdf", "application/pdf", [1, 2, 3]),
            "https://api.kapa/descadastro?token=abc"
        );

        // Act
        using var mime = Montagem.Montar(mensagem);
        var corpo = MillionSendEmailSender.Corpo(mime);

        // Assert
        corpo["from"].ShouldBe("\"Kapa\" <avisos@kapaformaturas.com.br>");
        corpo["to"].ShouldBe(new[] { "ana@exemplo.com" });
        corpo["html"].ShouldBe("<p>Oi</p>");
        corpo["reply_to"].ShouldBe(new[] { "suporte@kapaformaturas.com.br" });
        ((Dictionary<string, string?>)corpo["headers"]!)["List-Unsubscribe-Post"].ShouldBe("List-Unsubscribe=One-Click");
        var anexo = ((Dictionary<string, string?>[])corpo["attachments"]!).ShouldHaveSingleItem();
        anexo["filename"].ShouldBe("convite.pdf");
        anexo["content"].ShouldBe("AQID");
    }

    [Fact]
    public void Transacional_sai_sem_headers_nem_reply_to()
    {
        // Act
        using var mime = Montagem.Montar(new MensagemDeEmail("ana@exemplo.com", "Sua parcela", "<p>Oi</p>"));
        var corpo = MillionSendEmailSender.Corpo(mime);

        // Assert
        corpo.ContainsKey("headers").ShouldBeFalse();
        corpo.ContainsKey("reply_to").ShouldBeFalse();
        corpo.ContainsKey("attachments").ShouldBeFalse();
    }
}
