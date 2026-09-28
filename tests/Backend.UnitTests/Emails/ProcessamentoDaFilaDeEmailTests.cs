using Backend.Business.Abstractions;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Emails.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Backend.UnitTests.Emails;

/// <summary>
/// O ciclo de um lote da fila: reservar, enviar fora de transação e registrar o resultado.
/// </summary>
public sealed class ProcessamentoDaFilaDeEmailTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IEmailFilaRepository _fila = Substitute.For<IEmailFilaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IEmailSender _remetente = Substitute.For<IEmailSender>();

    public ProcessamentoDaFilaDeEmailTests() =>
        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<IReadOnlyList<EmailNaFila>>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<IReadOnlyList<EmailNaFila>>>>()(Ct));

    private ProcessamentoDaFilaDeEmail Servico(int tamanhoDoLote = 2) =>
        new(
            _fila,
            _unitOfWork,
            _remetente,
            Options.Create(new SmtpSettings { TamanhoDoLote = tamanhoDoLote, MaximoDeTentativas = 5 }),
            NullLogger<ProcessamentoDaFilaDeEmail>.Instance
        );

    private static EmailNaFila Reservado()
    {
        var email = new EmailNaFila
        {
            Para = "destino@exemplo.com",
            Assunto = "Assunto",
            CorpoHtml = "<p>Olá</p>",
        };
        email.MarcarEmEnvio(DateTime.UtcNow);

        return email;
    }

    [Fact]
    public async Task Fila_vazia_nao_envia_nem_grava()
    {
        // Arrange
        _fila.ReservarLote(2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var haMais = await Servico().ProcessarLote(Ct);

        // Assert
        haMais.ShouldBeFalse();
        await _remetente.DidNotReceive().EnviarAsync(Arg.Any<MensagemDeEmail>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lote_cheio_envia_marca_enviado_e_avisa_que_ha_mais()
    {
        // Arrange
        IReadOnlyList<EmailNaFila> lote = [Reservado(), Reservado()];
        _fila.ReservarLote(2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(lote);

        // Act
        var haMais = await Servico().ProcessarLote(Ct);

        // Assert
        haMais.ShouldBeTrue();
        lote.ShouldAllBe(email => email.Status == EEmailStatus.Enviado);
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Falha_no_envio_reagenda_o_email_e_o_lote_segue()
    {
        // Arrange
        var falho = Reservado();
        _fila.ReservarLote(2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([falho]);
        _remetente.EnviarAsync(Arg.Any<MensagemDeEmail>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("SMTP fora"));

        // Act
        var haMais = await Servico().ProcessarLote(Ct);

        // Assert
        haMais.ShouldBeFalse();
        falho.Status.ShouldBe(EEmailStatus.Pendente);
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }
}
