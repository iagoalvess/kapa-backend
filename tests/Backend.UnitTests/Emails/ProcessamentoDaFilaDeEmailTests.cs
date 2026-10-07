using System.Net;
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

    /// <summary>Recusa permanente do provedor não ganha nova tentativa: o mesmo 422 viria mais quatro vezes.</summary>
    [Fact]
    public async Task Recusa_permanente_desiste_na_primeira()
    {
        var recusado = Reservado();
        _fila.ReservarLote(2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([recusado]);
        _remetente
            .EnviarAsync(Arg.Any<MensagemDeEmail>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("MillionSend recusou o e-mail: 422 invalid to", null, HttpStatusCode.UnprocessableEntity));

        await Servico().ProcessarLote(Ct);

        recusado.Status.ShouldBe(EEmailStatus.Falhou);
    }

    /// <summary>A preferência é conferida no envio (Sprint 40): quem saiu depois de enfileirar não recebe.</summary>
    [Fact]
    public async Task Marketing_de_quem_saiu_e_descartado_e_o_transacional_sai()
    {
        // Arrange
        var saiu = Guid.CreateVersion7();
        var ficou = Guid.CreateVersion7();
        var paraQuemSaiu = Marketing(saiu);
        var paraQuemFicou = Marketing(ficou);
        var transacional = Reservado();
        _fila.ReservarLote(3, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([paraQuemSaiu, paraQuemFicou, transacional]);
        _fila
            .ListarQueRecebemMarketing(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { ficou });

        // Act
        await Servico(tamanhoDoLote: 3).ProcessarLote(Ct);

        // Assert
        paraQuemSaiu.Status.ShouldBe(EEmailStatus.Descartado);
        paraQuemFicou.Status.ShouldBe(EEmailStatus.Enviado);
        transacional.Status.ShouldBe(EEmailStatus.Enviado);
        await _remetente.Received(2).EnviarAsync(Arg.Any<MensagemDeEmail>(), Arg.Any<CancellationToken>());
        await _remetente
            .Received(1)
            .EnviarAsync(Arg.Is<MensagemDeEmail>(m => m.LinkDeDescadastro == "https://api/descadastro"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lote_so_transacional_nao_consulta_preferencia()
    {
        // Arrange
        _fila.ReservarLote(2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([Reservado()]);

        // Act
        await Servico().ProcessarLote(Ct);

        // Assert
        await _fila.DidNotReceive().ListarQueRecebemMarketing(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    private static EmailNaFila Marketing(Guid usuarioId)
    {
        var email = Reservado();
        email.Categoria = ECategoriaDeEmail.Marketing;
        email.UsuarioId = usuarioId;
        email.LinkDeDescadastro = "https://api/descadastro";

        return email;
    }
}
