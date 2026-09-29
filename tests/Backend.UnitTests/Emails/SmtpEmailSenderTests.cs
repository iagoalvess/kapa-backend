using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Emails.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Backend.UnitTests.Emails;

/// <summary>
/// A mensagem como sai para o servidor: o marketing leva os cabeçalhos da RFC 8058 e o remetente próprio; o
/// transacional, nunca (Sprint 40).
/// </summary>
public sealed class SmtpEmailSenderTests
{
    private static readonly SmtpEmailSender Remetente = new(
        Options.Create(
            new SmtpSettings
            {
                RemetenteEmail = "avisos@kapaformaturas.com.br",
                RemetenteNome = "Kapa",
                RemetenteDeMarketingEmail = "novidades@novidades.kapaformaturas.com.br",
                ResponderMarketingPara = "suporte@kapaformaturas.com.br",
            }
        ),
        NullLogger<SmtpEmailSender>.Instance
    );

    [Fact]
    public void Marketing_leva_list_unsubscribe_one_click_remetente_e_resposta_proprios()
    {
        using var mime = Remetente.Montar(
            new MensagemDeEmail("ana@exemplo.com", "Assunto", "<p>Oi</p>", LinkDeDescadastro: "https://api.kapa/descadastro?token=abc")
        );

        mime.Headers["List-Unsubscribe"].ShouldBe("<https://api.kapa/descadastro?token=abc>");
        mime.Headers["List-Unsubscribe-Post"].ShouldBe("List-Unsubscribe=One-Click");
        mime.From.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("novidades@novidades.kapaformaturas.com.br");
        mime.ReplyTo.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("suporte@kapaformaturas.com.br");
    }

    [Fact]
    public void Transacional_nao_leva_list_unsubscribe()
    {
        using var mime = Remetente.Montar(new MensagemDeEmail("ana@exemplo.com", "Sua parcela vence amanhã", "<p>Oi</p>"));

        mime.Headers.Contains("List-Unsubscribe").ShouldBeFalse();
        mime.Headers.Contains("List-Unsubscribe-Post").ShouldBeFalse();
        mime.From.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("avisos@kapaformaturas.com.br");
        mime.ReplyTo.Count.ShouldBe(0);
    }
}
