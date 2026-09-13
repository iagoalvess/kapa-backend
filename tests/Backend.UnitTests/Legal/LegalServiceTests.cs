using Backend.Business.Abstractions;
using Backend.Business.Legal.Interfaces;
using Backend.Business.Legal.Models;
using Backend.Business.Legal.Services;
using Backend.Business.Legal.Validators;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Legal;

/// <summary>
/// Regras do consentimento que não dependem de banco: qual linha vale e quando o aceite é recusado.
/// </summary>
public sealed class LegalServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Ontem = DateTime.UtcNow.AddDays(-1);

    private readonly ILegalRepository _repositorio = Substitute.For<ILegalRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private LegalService Servico => new(_repositorio, new RegistrarAceiteValidator(), _unitOfWork);

    [Fact]
    public async Task Versao_nova_dos_termos_vira_pendencia()
    {
        Vigentes(Documento(TipoDeDocumento.TermosDeUso, "2"), Documento(TipoDeDocumento.PoliticaDePrivacidade, "1"));
        Historico(Aceite(TipoDeDocumento.TermosDeUso, "1"), Aceite(TipoDeDocumento.PoliticaDePrivacidade, "1"));

        var aceites = (await Servico.ObterMeusAceites(Guid.CreateVersion7(), Ct)).Valor;

        aceites.Pendencias.ShouldBe([new AceitePendente(TipoDeDocumento.TermosDeUso, "2")]);
    }

    /// <summary>Vale a linha mais recente: aceite seguido de revogação é pendência.</summary>
    [Fact]
    public async Task Revogacao_posterior_ao_aceite_e_pendencia()
    {
        Vigentes(Documento(TipoDeDocumento.PoliticaDePrivacidade, "1"));
        Historico(Aceite(TipoDeDocumento.PoliticaDePrivacidade, "1", revogado: true), Aceite(TipoDeDocumento.PoliticaDePrivacidade, "1"));

        var aceites = (await Servico.ObterMeusAceites(Guid.CreateVersion7(), Ct)).Valor;

        aceites.Pendencias.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Novo_aceite_depois_da_revogacao_resolve_a_pendencia()
    {
        Vigentes(Documento(TipoDeDocumento.PoliticaDePrivacidade, "1"));
        Historico(Aceite(TipoDeDocumento.PoliticaDePrivacidade, "1"), Aceite(TipoDeDocumento.PoliticaDePrivacidade, "1", revogado: true));

        var aceites = (await Servico.ObterMeusAceites(Guid.CreateVersion7(), Ct)).Valor;

        aceites.Pendencias.ShouldBeEmpty();
    }

    /// <summary>Recusa um e não grava nenhum: meio aceite no rastreador esperaria o próximo commit.</summary>
    [Fact]
    public async Task Versao_desatualizada_recusa_o_pedido_inteiro_sem_gravar()
    {
        var velha = Documento(TipoDeDocumento.TermosDeUso, "1");
        Vigentes(Documento(TipoDeDocumento.TermosDeUso, "2"), Documento(TipoDeDocumento.PoliticaDePrivacidade, "1"));
        _repositorio.ObterVersao(TipoDeDocumento.TermosDeUso, "1", Arg.Any<CancellationToken>()).Returns(velha);

        var resultado = await Servico.RegistrarAceites(
            Guid.CreateVersion7(),
            [new AceiteDeDocumento(TipoDeDocumento.PoliticaDePrivacidade, "1"), new AceiteDeDocumento(TipoDeDocumento.TermosDeUso, "1")],
            new OrigemDoAceite("10.0.0.1", "teste"),
            Ct
        );

        resultado.PrimeiroErro.Codigo.ShouldBe("legal.versao_desatualizada");
        await _repositorio.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Documento_desconhecido_e_nao_encontrado()
    {
        Vigentes(Documento(TipoDeDocumento.TermosDeUso, "1"));

        var resultado = await Servico.RegistrarAceites(
            Guid.CreateVersion7(),
            [new AceiteDeDocumento("Regulamento", "1")],
            new OrigemDoAceite(null, null),
            Ct
        );

        resultado.PrimeiroErro.Codigo.ShouldBe("legal.documento_nao_encontrado");
    }

    /// <summary>O cabeçalho é escrito pelo cliente e não tem teto; a coluna tem.</summary>
    [Fact]
    public async Task Aceite_valido_grava_uma_linha_por_documento_com_user_agent_truncado()
    {
        var termos = Documento(TipoDeDocumento.TermosDeUso, "1");
        Vigentes(termos);

        var resultado = await Servico.RegistrarAceites(
            Guid.CreateVersion7(),
            [new AceiteDeDocumento("termosdeuso", "1"), new AceiteDeDocumento(TipoDeDocumento.TermosDeUso, "1")],
            new OrigemDoAceite("10.0.0.1", new string('x', 2000)),
            Ct
        );

        resultado.Sucesso.ShouldBeTrue();
        await _repositorio
            .Received(1)
            .Adicionar(
                Arg.Is<ConsentimentoRegistrado>(c => c.DocumentoLegalId == termos.Id && c.Versao == "1" && c.UserAgent.Length == 512 && !c.Revogado),
                Arg.Any<CancellationToken>()
            );
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    private void Vigentes(params VersaoDeDocumento[] documentos) =>
        _repositorio.ListarVigentes(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(documentos);

    private void Historico(params ConsentimentoDoUsuario[] linhasDaMaisRecente) =>
        _repositorio.ListarConsentimentos(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(linhasDaMaisRecente);

    private static VersaoDeDocumento Documento(string tipo, string versao) => new(Guid.CreateVersion7(), tipo, versao, "texto", Ontem);

    private static ConsentimentoDoUsuario Aceite(string tipo, string versao, bool revogado = false) => new(tipo, versao, Ontem, revogado);
}
