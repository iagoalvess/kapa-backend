using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Formaturas.Services;
using Backend.Business.Formaturas.Validators;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Formaturas;

/// <summary>
/// Regras da formatura que dependem de estado: rascunho pendente, edição e encerramento.
/// </summary>
public sealed class FormaturaServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();
    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly IAssinaturaRepository _assinaturas = Substitute.For<IAssinaturaRepository>();
    private readonly IProvedorDeAssinatura _provedor = Substitute.For<IProvedorDeAssinatura>();
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public FormaturaServiceTests()
    {
        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result<ParDeTokens>>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<Result<ParDeTokens>>>>()(CancellationToken.None));
    }

    private FormaturaService Servico => new(_formaturas, _vinculos, _assinaturas, _provedor, _auth, new DadosDaFormaturaValidator(), _unitOfWork);

    private static DadosDaFormatura Dados(int? ano = null) =>
        new("Medicina 2027.1 — UFPR", "UFPR", "Medicina", ano ?? DateTime.UtcNow.Year + 1, 1, null, 80);

    [Fact]
    public async Task Criar_com_rascunho_pendente_devolve_conflito_sem_gravar()
    {
        // Arrange
        _formaturas.ExisteRascunhoCriadoPor(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var resultado = await Servico.Criar(Guid.CreateVersion7(), Dados(), "refresh", null, Ct);

        // Assert
        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("formatura.rascunho_pendente");
        await _formaturas.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    [Fact]
    public async Task Criar_grava_formatura_e_vinculo_de_presidente_e_emite_sessao_na_formatura()
    {
        var usuarioId = Guid.CreateVersion7();
        Formatura? criada = null;
        await _formaturas.Adicionar(Arg.Do<Formatura>(f => criada = f), Arg.Any<CancellationToken>());
        _auth
            .EmitirSessaoDeFormatura(default, default, default!, default!, default, Ct)
            .ReturnsForAnyArgs(new ParDeTokens("a", DateTime.UtcNow, "r"));

        var resultado = await Servico.Criar(usuarioId, Dados(), "refresh", null, Ct);

        resultado.Sucesso.ShouldBeTrue();
        criada.ShouldNotBeNull();
        criada.Status.ShouldBe(StatusDaFormatura.Rascunho);
        criada.CriadoPorUsuarioId.ShouldBe(usuarioId);
        await _vinculos
            .Received(1)
            .Adicionar(
                Arg.Is<VinculoDeFormatura>(v => v.UsuarioId == usuarioId && v.FormaturaId == criada.Id && v.Papel == PapelNaFormatura.Presidente),
                Arg.Any<CancellationToken>()
            );
        await _auth
            .Received(1)
            .EmitirSessaoDeFormatura(usuarioId, criada.Id, PapelNaFormatura.Presidente, "refresh", null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(DadosDaFormaturaValidator.AnosAFrente + 1)]
    public async Task Ano_fora_da_janela_devolve_validacao(int deslocamento)
    {
        var resultado = await Servico.Criar(Guid.CreateVersion7(), Dados(DateTime.UtcNow.Year + deslocamento), "refresh", null, Ct);

        resultado.Erros.ShouldHaveSingleItem().Campo.ShouldBe("ano");
    }

    [Theory]
    [InlineData(StatusDaFormatura.Suspensa)]
    [InlineData(StatusDaFormatura.Encerrada)]
    public async Task Atualizar_formatura_em_modo_leitura_devolve_inativa(StatusDaFormatura status)
    {
        _formaturas.ObterParaEdicao(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Em(status));

        var resultado = await Servico.Atualizar(Guid.CreateVersion7(), Dados(), Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("formatura.inativa");
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Encerrar_rascunho_devolve_transicao_invalida()
    {
        _formaturas.ObterParaEdicao(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Em(StatusDaFormatura.Rascunho));

        var resultado = await Servico.Encerrar(Guid.CreateVersion7(), Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("formatura.transicao_invalida");
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Encerrar_ativa_grava_encerrada()
    {
        var formatura = Em(StatusDaFormatura.Ativa);
        _formaturas.ObterParaEdicao(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(formatura);

        var resultado = await Servico.Encerrar(Guid.CreateVersion7(), Ct);

        resultado.Sucesso.ShouldBeTrue();
        formatura.Status.ShouldBe(StatusDaFormatura.Encerrada);
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Descartar tira a turma da lista de todos e expira o checkout que ficou aberto.</summary>
    [Fact]
    public async Task Descartar_aguardando_pagamento_desativa_os_vinculos_e_expira_o_checkout()
    {
        var formatura = Em(StatusDaFormatura.AguardandoPagamento);
        var presidente = new VinculoDeFormatura { Papel = PapelNaFormatura.Presidente, Ativo = true };
        var tesoureiro = new VinculoDeFormatura { Papel = PapelNaFormatura.Tesoureiro, Ativo = true };
        _formaturas.ObterParaEdicao(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(formatura);
        _vinculos.ListarAtivosParaEdicao(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([presidente, tesoureiro]);
        _assinaturas.ObterMaisRecenteParaEdicao(Arg.Any<CancellationToken>()).Returns(new Assinatura { IdExterno = "sessao-1" });
        _provedor.Cancelar("sessao-1", Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var resultado = await Servico.Descartar(Guid.CreateVersion7(), Ct);

        resultado.Sucesso.ShouldBeTrue();
        formatura.Status.ShouldBe(StatusDaFormatura.Descartada);
        presidente.Ativo.ShouldBeFalse();
        tesoureiro.Ativo.ShouldBeFalse();
        await _provedor.Received(1).Cancelar("sessao-1", Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Turma que já pagou encerra, não descarta.</summary>
    [Fact]
    public async Task Descartar_ativa_devolve_transicao_invalida_sem_gravar()
    {
        var formatura = Em(StatusDaFormatura.Ativa);
        _formaturas.ObterParaEdicao(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(formatura);

        var resultado = await Servico.Descartar(Guid.CreateVersion7(), Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("formatura.transicao_invalida");
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    /// <summary>Com a renovação ligada, encerrar deixaria o PSP cobrando uma turma fechada.</summary>
    [Fact]
    public async Task Encerrar_com_assinatura_renovando_devolve_conflito_sem_gravar()
    {
        var formatura = Em(StatusDaFormatura.Ativa);
        _formaturas.ObterParaEdicao(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(formatura);
        _assinaturas
            .ObterDetalheDaMaisRecente(Arg.Any<CancellationToken>())
            .Returns(new AssinaturaDetalhe(Guid.CreateVersion7(), StatusDaAssinatura.Ativa, null!, null, null, null, DateTime.UtcNow));

        var resultado = await Servico.Encerrar(Guid.CreateVersion7(), Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("formatura.assinatura_ativa");
        formatura.Status.ShouldBe(StatusDaFormatura.Ativa);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    private static Formatura Em(StatusDaFormatura status)
    {
        var formatura = new Formatura();
        StatusDaFormatura[] caminho = status switch
        {
            StatusDaFormatura.Rascunho => [],
            StatusDaFormatura.AguardandoPagamento => [StatusDaFormatura.AguardandoPagamento],
            StatusDaFormatura.Ativa => [StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Ativa],
            StatusDaFormatura.Suspensa => [StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Ativa, StatusDaFormatura.Suspensa],
            _ => [StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Ativa, StatusDaFormatura.Encerrada],
        };

        foreach (var passo in caminho)
            formatura.Transicionar(passo);

        return formatura;
    }
}
