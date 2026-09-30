using System.Text;
using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Pagamentos.Services;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Pagamentos;

/// <summary>
/// O recibo (Sprint 22): o próprio formando vê o CPF inteiro, a gestão mascarado, os demais recebem 404, e o titular
/// é o da conta no dia do pagamento.
/// </summary>
public sealed class ReciboServiceTests
{
    private static readonly Guid FormaturaId = Guid.CreateVersion7();
    private static readonly MembroDoPerfil Ana = new(Guid.CreateVersion7(), Guid.CreateVersion7(), "Ana", "ana@turma.dev", PapelNaFormatura.Formando);
    private static readonly MembroDoPerfil Bruno = new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "Bruno",
        "bruno@turma.dev",
        PapelNaFormatura.Formando
    );
    private static readonly MembroDoPerfil Tesoureira = new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "Tesa",
        "tesa@turma.dev",
        PapelNaFormatura.Tesoureiro
    );

    private static readonly ChavePixDaConta ChaveDaComissao = new(TipoDeChavePix.Cpf, "52998224725", "Comissão Medicina", "Curitiba");

    private static readonly MeiosDaConta SoPix = new(ChaveDaComissao, null, null);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRecebimentoRepository _recebimentos = Substitute.For<IRecebimentoRepository>();
    private readonly IContaDeRecebimentoRepository _contas = Substitute.For<IContaDeRecebimentoRepository>();
    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();

    private readonly DateOnly _hoje = DataUtils.Hoje();

    public ReciboServiceTests()
    {
        foreach (var membro in new[] { Ana, Bruno, Tesoureira })
            _perfis.ObterMembro(FormaturaId, membro.UsuarioId, Arg.Any<CancellationToken>()).Returns(membro);
    }

    private ReciboService Servico => new(_recebimentos, _contas, _perfis);

    private static ContaDeRecebimentoDetalhe Conta(bool conferida) => Conta(conferida, SoPix);

    private static ContaDeRecebimentoDetalhe Conta(bool conferida, MeiosDaConta meios) =>
        new(meios, DateTime.UtcNow, conferida ? DateTime.UtcNow : null, null);

    /// <summary>Uma parcela paga da Ana, como a leitura a devolve.</summary>
    private ParcelaResumo DaAna(DateOnly vencimento) =>
        new(
            Guid.CreateVersion7(),
            Ana.VinculoId,
            Ana.UsuarioId,
            Ana.Nome,
            Guid.CreateVersion7(),
            TipoDeCobranca.Mensalidade,
            null,
            3,
            24,
            vencimento,
            350_000,
            Parcela.StatusNoDia(StatusDaParcela.Paga, vencimento, _hoje),
            false
        );

    /// <summary>Um recebimento da Ana, como a leitura do recibo o devolve.</summary>
    private DadosDoRecibo ReciboDaAna(long recebido = 350_000, bool estornado = false)
    {
        var recibo = new DadosDoRecibo(
            Guid.CreateVersion7(),
            FormaturaId,
            "Medicina 2027",
            "UFPR",
            DaAna(_hoje.AddDays(-3)),
            "52998224725",
            FormaDePagamento.Pix,
            recebido,
            350_000,
            _hoje.AddDays(-3),
            "Tesa",
            new DateTime(2026, 9, 15, 13, 0, 0, DateTimeKind.Utc),
            estornado
        );
        _recebimentos.ObterParaRecibo(recibo.RecebimentoId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(recibo);
        foreach (var membro in new[] { Ana, Bruno, Tesoureira })
            _perfis.ObterTitular(FormaturaId, membro.UsuarioId, Arg.Any<CancellationToken>()).Returns(membro);

        return recibo;
    }

    private static string TextoDoPdf(ArquivoParaDownload arquivo) => Encoding.ASCII.GetString(((MemoryStream)arquivo.Conteudo).ToArray());

    [Fact]
    public async Task Recibo_do_proprio_traz_cpf_inteiro_e_o_da_gestao_mascarado()
    {
        // Arrange
        var recibo = ReciboDaAna();
        _contas.ObterMeiosVigentesEm(FormaturaId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(SoPix);

        // Act
        var proprio = await Servico.Obter(FormaturaId, Ana.UsuarioId, recibo.RecebimentoId, Ct);
        var daGestao = await Servico.Obter(FormaturaId, Tesoureira.UsuarioId, recibo.RecebimentoId, Ct);

        // Assert
        proprio.Valor.ContentType.ShouldBe("application/pdf");
        TextoDoPdf(proprio.Valor).ShouldContain("529.982.247-25");
        TextoDoPdf(daGestao.Valor).ShouldContain("***.982.247-**");
        TextoDoPdf(daGestao.Valor).ShouldNotContain("529.982.247-25");
    }

    [Fact]
    public async Task Recibo_de_outro_formando_e_404_e_o_estornado_e_409()
    {
        // Arrange
        var daAna = ReciboDaAna();
        var estornado = ReciboDaAna(estornado: true);

        // Act
        var deOutro = await Servico.Obter(FormaturaId, Bruno.UsuarioId, daAna.RecebimentoId, Ct);
        var inexistente = await Servico.Obter(FormaturaId, Ana.UsuarioId, Guid.CreateVersion7(), Ct);
        var desfeito = await Servico.Obter(FormaturaId, Ana.UsuarioId, estornado.RecebimentoId, Ct);

        // Assert
        deOutro.PrimeiroErro.Tipo.ShouldBe(ETipoErro.NaoEncontrado);
        inexistente.PrimeiroErro.Codigo.ShouldBe(deOutro.PrimeiroErro.Codigo);
        desfeito.PrimeiroErro.Codigo.ShouldBe("pagamento.recebimento_estornado");
        desfeito.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Conflito);
    }

    [Fact]
    public async Task Recibo_nomeia_o_titular_do_dia_do_pagamento_e_nao_o_de_hoje()
    {
        // Arrange
        var recibo = ReciboDaAna();
        var chaveNova = new ChavePixDaConta(TipoDeChavePix.Email, "novo@turma.dev", "Fulano Trocado", "Curitiba");
        _contas.ObterMeiosVigentesEm(FormaturaId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(SoPix);
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: false, new MeiosDaConta(chaveNova, null, null)));

        // Act
        var texto = TextoDoPdf((await Servico.Obter(FormaturaId, Ana.UsuarioId, recibo.RecebimentoId, Ct)).Valor);

        // Assert
        texto.ShouldContain(@"Comiss\343o Medicina");
        texto.ShouldNotContain("Fulano Trocado");
        await _contas.Received(1).ObterMeiosVigentesEm(FormaturaId, recibo.BaixadoEm, Ct);
    }

    [Fact]
    public async Task Recibo_sem_trilha_cai_na_conta_atual()
    {
        // Arrange
        var recibo = ReciboDaAna();
        _contas.ObterDetalhe(Arg.Any<CancellationToken>()).Returns(Conta(conferida: true));

        // Act
        var texto = TextoDoPdf((await Servico.Obter(FormaturaId, Ana.UsuarioId, recibo.RecebimentoId, Ct)).Valor);

        // Assert
        texto.ShouldContain(@"Comiss\343o Medicina");
    }
}
