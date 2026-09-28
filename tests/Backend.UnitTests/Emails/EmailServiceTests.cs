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

    /// <summary>O anexo (o PDF do convite) entra na mesma linha da fila — e a entrega o lê de lá.</summary>
    [Fact]
    public async Task O_anexo_vai_com_o_email_para_a_fila()
    {
        var pdf = new AnexoDoEmail("convite-MED27-7QK4.pdf", "application/pdf", [0x25, 0x50, 0x44, 0x46]);

        await Criar().Enfileirar(new NovoEmail("destino@exemplo.com", "Seu convite", "<p>Olá</p>", Anexo: pdf), Ct);

        await _repositorio
            .Received(1)
            .Adicionar(
                Arg.Is<EmailNaFila>(e => e.Anexo != null && e.Anexo.Nome == pdf.Nome && e.Anexo.Conteudo.SequenceEqual(pdf.Conteudo)),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>Anexo grande não entra na fila: o teto é o que mantém os bytes no banco uma escolha razoável.</summary>
    [Fact]
    public async Task Anexo_acima_de_2_mb_e_recusado()
    {
        var grande = new AnexoDoEmail("grande.pdf", "application/pdf", new byte[AnexoDoEmail.TamanhoMaximo + 1]);

        var resultado = await Criar().Enfileirar(new NovoEmail("destino@exemplo.com", "Assunto", "<p>Olá</p>", Anexo: grande), Ct);

        resultado.Falhou.ShouldBeTrue();
        await _repositorio.DidNotReceive().Adicionar(Arg.Any<EmailNaFila>(), Arg.Any<CancellationToken>());
    }
}
