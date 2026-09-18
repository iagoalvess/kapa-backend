using Backend.Business.Abstractions;
using Backend.Business.Common;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Leads.Interfaces;
using Backend.Business.Leads.Models;
using Backend.Business.Leads.Services;
using Backend.Business.Leads.Settings;
using Backend.Business.Leads.Validators;
using Backend.Business.Legal.Interfaces;
using Backend.Business.Legal.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Leads;

/// <summary>
/// O formulário de contato da página institucional: as três defesas e a prova do consentimento.
/// </summary>
public sealed class LeadServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ILeadRepository _leads = Substitute.For<ILeadRepository>();
    private readonly ILegalRepository _legal = Substitute.For<ILegalRepository>();
    private readonly IEmailService _emails = Substitute.For<IEmailService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public LeadServiceTests() =>
        _legal
            .ListarVigentes(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([
                new VersaoDeDocumento(Guid.CreateVersion7(), TipoDeDocumento.TermosDeUso, "1", "…", DateTime.UtcNow),
                new VersaoDeDocumento(Guid.CreateVersion7(), TipoDeDocumento.PoliticaDePrivacidade, "3", "…", DateTime.UtcNow),
            ]);

    private LeadService Servico =>
        new(
            _leads,
            _legal,
            _emails,
            new NovoLeadValidator(),
            Options.Create(new AplicacaoSettings { Nome = "Kapa" }),
            Options.Create(new LeadsSettings { EmailDoComercial = "comercial@kapa.dev" }),
            _unitOfWork,
            NullLogger<LeadService>.Instance
        );

    private static NovoLead Formulario(string? sobrenome = null, bool aceita = true) =>
        new(
            " Ana Souza ",
            "  ANA@Exemplo.COM ",
            "(41) 99876-5432",
            "UFPR",
            "Medicina",
            82,
            "2027-12",
            "Queremos organizar a formatura.",
            aceita,
            "instagram",
            "social",
            "lancamento",
            sobrenome
        );

    /// <summary>
    /// Honeypot preenchido responde sucesso e não grava.
    /// </summary>
    /// <remarks>Um 400 ensinaria o robô qual campo deixar em branco na próxima tentativa.</remarks>
    [Fact]
    public async Task Honeypot_preenchido_responde_sucesso_sem_gravar()
    {
        // Act
        var resultado = await Servico.Registrar(Formulario(sobrenome: "robô"), new OrigemDoContato(null, null), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        await _leads.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Sem_aceitar_a_privacidade_recusa_com_o_codigo_do_contrato()
    {
        var resultado = await Servico.Registrar(Formulario(aceita: false), new OrigemDoContato(null, null), Ct);

        resultado.Erros.ShouldContain(e => e.Codigo == "lead.privacidade_nao_aceita");
        await _leads.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>O mesmo e-mail dentro da janela não vira um segundo contato na caixa do comercial.</summary>
    [Fact]
    public async Task Contato_repetido_na_janela_responde_sucesso_sem_gravar()
    {
        _leads.JaRegistrado("ana@exemplo.com", Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(true);

        var resultado = await Servico.Registrar(Formulario(), new OrigemDoContato(null, null), Ct);

        resultado.Sucesso.ShouldBeTrue();
        await _leads.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>
    /// Quem preenche o formulário é titular de dados: o consentimento é gravado com a versão
    /// vigente da Política, o momento, o IP e o navegador — a mesma prova da Sprint 1.
    /// </summary>
    [Fact]
    public async Task Contato_valido_grava_o_consentimento_e_normaliza_o_que_veio_do_formulario()
    {
        // Arrange
        Lead? gravado = null;
        await _leads.Adicionar(Arg.Do<Lead>(l => gravado = l), Arg.Any<CancellationToken>());

        // Act
        var resultado = await Servico.Registrar(Formulario(), new OrigemDoContato("203.0.113.7", "Mozilla/5.0"), Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        gravado.ShouldNotBeNull();
        gravado.Nome.ShouldBe("Ana Souza");
        gravado.Email.ShouldBe("ana@exemplo.com");
        gravado.Telefone.ShouldBe("+5541998765432");
        gravado.PrevisaoDeColacao.ShouldBe(new DateOnly(2027, 12, 1));
        gravado.Origem.ShouldBe("instagram");
        gravado.PrivacidadeVersao.ShouldBe("3");
        gravado.ConsentidoEm.ShouldNotBe(default);
        gravado.EnderecoIp.ShouldBe("203.0.113.7");
        gravado.UserAgent.ShouldBe("Mozilla/5.0");
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Contato_valido_avisa_o_comercial()
    {
        await Servico.Registrar(Formulario(), new OrigemDoContato(null, null), Ct);

        await _emails
            .Received(1)
            .Enfileirar(Arg.Is<NovoEmail>(e => e.Para == "comercial@kapa.dev" && e.Assunto.Contains("UFPR")), Arg.Any<CancellationToken>());
    }

    /// <summary>Sem caixa configurada o contato continua sendo gravado: o painel é a fonte da verdade.</summary>
    [Fact]
    public async Task Sem_email_do_comercial_o_contato_ainda_e_gravado()
    {
        var servico = new LeadService(
            _leads,
            _legal,
            _emails,
            new NovoLeadValidator(),
            Options.Create(new AplicacaoSettings { Nome = "Kapa" }),
            Options.Create(new LeadsSettings()),
            _unitOfWork,
            NullLogger<LeadService>.Instance
        );

        var resultado = await servico.Registrar(Formulario(), new OrigemDoContato(null, null), Ct);

        resultado.Sucesso.ShouldBeTrue();
        await _leads.Received(1).Adicionar(Arg.Any<Lead>(), Arg.Any<CancellationToken>());
        await _emails.DidNotReceiveWithAnyArgs().Enfileirar(default!, Ct);
    }
}
