using Backend.Business.Emails.Models;
using Shouldly;

namespace Backend.UnitTests.Emails;

/// <summary>
/// Cobre a política de retentativa, que é a regra real do módulo de e-mail.
/// </summary>
/// <remarks>
/// Testar aqui, e não no job, é o ganho de a entidade ser dona das próprias transições: a
/// política é verificada sem SMTP, sem banco e sem worker.
/// </remarks>
public sealed class EmailNaFilaTests
{
    private static readonly DateTime Agora = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    private static EmailNaFila Novo() =>
        new()
        {
            Para = "destino@exemplo.com",
            Assunto = "Assunto",
            CorpoHtml = "<p>Olá</p>",
        };

    [Fact]
    public void Um_email_novo_nasce_pendente_e_pronto_para_envio()
    {
        var email = Novo();

        email.Status.ShouldBe(EEmailStatus.Pendente);
        email.Tentativas.ShouldBe(0);
        email.ProximaTentativaEm.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
    }

    [Fact]
    public void Reservar_conta_a_tentativa_antes_do_envio()
    {
        var email = Novo();

        email.MarcarEmEnvio(Agora);

        email.Status.ShouldBe(EEmailStatus.Enviando);
        email.Tentativas.ShouldBe(1);
    }

    [Fact]
    public void Envio_aceito_limpa_o_erro_anterior()
    {
        var email = Novo();
        email.MarcarEmEnvio(Agora);
        email.RegistrarFalha("recusado", Agora, maximoDeTentativas: 5);

        email.MarcarEmEnvio(Agora);
        email.MarcarEnviado(Agora);

        email.Status.ShouldBe(EEmailStatus.Enviado);
        email.EnviadoEm.ShouldBe(Agora);
        email.UltimoErro.ShouldBeNull();
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 9)]
    [InlineData(3, 27)]
    [InlineData(4, 81)]
    public void A_espera_entre_tentativas_cresce_exponencialmente(int tentativa, int minutosEsperados)
    {
        var email = Novo();

        for (var i = 0; i < tentativa; i++)
        {
            email.MarcarEmEnvio(Agora);
            email.RegistrarFalha("servidor fora", Agora, maximoDeTentativas: 10);
        }

        email.ProximaTentativaEm.ShouldBe(Agora.AddMinutes(minutosEsperados));
        email.Status.ShouldBe(EEmailStatus.Pendente);
    }

    [Fact]
    public void Esgotadas_as_tentativas_o_email_para_de_ser_reagendado()
    {
        var email = Novo();

        for (var i = 0; i < 3; i++)
        {
            email.MarcarEmEnvio(Agora);
            email.RegistrarFalha("servidor fora", Agora, maximoDeTentativas: 3);
        }

        email.Status.ShouldBe(EEmailStatus.Falhou);
        email.Tentativas.ShouldBe(3);
    }

    [Fact]
    public void O_erro_e_preservado_para_diagnostico_e_truncado_ao_limite_da_coluna()
    {
        var email = Novo();
        email.MarcarEmEnvio(Agora);

        email.RegistrarFalha(new string('x', 5000), Agora, maximoDeTentativas: 5);

        email.UltimoErro.ShouldNotBeNull();
        email.UltimoErro.Length.ShouldBe(1000);
    }
}
