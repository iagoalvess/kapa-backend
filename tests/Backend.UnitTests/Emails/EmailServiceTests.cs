using Backend.Business.Abstractions;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Emails.Validators;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Emails;

/// <summary>
/// Garante que enfileirar é só enfileirar.
/// </summary>
public sealed class EmailServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IEmailFilaRepository _repositorio = Substitute.For<IEmailFilaRepository>();

    private EmailService Criar() => new(_repositorio, new NovoEmailValidator());

    [Fact]
    public async Task Enfileirar_coloca_o_email_na_fila_como_pendente()
    {
        var resultado = await Criar().Enfileirar(new NovoEmail("destino@exemplo.com", "Bem-vindo", "<p>Olá</p>"), Ct);

        resultado.Sucesso.ShouldBeTrue();

        await _repositorio
            .Received(1)
            .Adicionar(Arg.Is<EmailNaFila>(e => e.Para == "destino@exemplo.com" && e.Status == EEmailStatus.Pendente), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Endereco_invalido_e_recusado_no_momento_de_enfileirar()
    {
        var resultado = await Criar().Enfileirar(new NovoEmail("nao-e-email", "Assunto", "<p>Olá</p>"), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Validacao);
        resultado.PrimeiroErro.Campo.ShouldBe("para");

        await _repositorio.DidNotReceive().Adicionar(Arg.Any<EmailNaFila>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_prioridade_informada_chega_na_fila()
    {
        await Criar().Enfileirar(new NovoEmail("destino@exemplo.com", "Redefinir senha", "<p>Link</p>", EEmailPrioridade.Alta), Ct);

        await _repositorio.Received(1).Adicionar(Arg.Is<EmailNaFila>(e => e.Prioridade == EEmailPrioridade.Alta), Arg.Any<CancellationToken>());
    }
}
