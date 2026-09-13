using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Services;
using Backend.Business.Assinaturas.Settings;
using Backend.Business.Assinaturas.Validators;
using Backend.Business.Common;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Assinaturas;

/// <summary>
/// Checkout e cancelamento: o que depende do status da formatura e da resposta do provedor.
/// </summary>
public sealed class AssinaturaServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IAssinaturaRepository _assinaturas = Substitute.For<IAssinaturaRepository>();
    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();
    private readonly IProvedorDeAssinatura _provedor = Substitute.For<IProvedorDeAssinatura>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static readonly Plano Completo = new()
    {
        Codigo = "completo",
        Nome = "Completo",
        PrecoEmCentavos = 34990,
        LimiteDeFormandos = 150,
    };

    public AssinaturaServiceTests()
    {
        _assinaturas.ObterPlanoAtivo("completo", Arg.Any<CancellationToken>()).Returns(Completo);
        _provedor
            .CriarCheckout(Arg.Any<PedidoDeCheckout>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new SessaoDeCheckout("sessao-1", "https://psp/checkout/sessao-1")));
    }

    private AssinaturaService Servico =>
        new(
            _assinaturas,
            _formaturas,
            _provedor,
            new IniciarCheckoutValidator(),
            Options.Create(new AssinaturaSettings()),
            Options.Create(new AplicacaoSettings { UrlDoFrontend = "https://app.kapa" }),
            _unitOfWork
        );

    private Formatura FormaturaEm(StatusDaFormatura status)
    {
        var formatura = new Formatura();

        foreach (var passo in new[] { StatusDaFormatura.AguardandoPagamento, StatusDaFormatura.Ativa, StatusDaFormatura.Suspensa })
        {
            if (formatura.Status == status)
                break;

            formatura.Transicionar(passo);
        }

        _formaturas.ObterParaEdicao(formatura.Id, Arg.Any<CancellationToken>()).Returns(formatura);

        return formatura;
    }

    [Fact]
    public async Task Checkout_em_rascunho_cria_pendente_e_passa_a_aguardando_pagamento()
    {
        // Arrange
        var formatura = FormaturaEm(StatusDaFormatura.Rascunho);
        Assinatura? criada = null;
        await _assinaturas.Adicionar(Arg.Do<Assinatura>(a => criada = a), Arg.Any<CancellationToken>());

        // Act
        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("completo"), Ct);

        // Assert
        resultado.Valor.Url.ShouldBe("https://psp/checkout/sessao-1");
        criada.ShouldNotBeNull();
        criada.Status.ShouldBe(StatusDaAssinatura.Pendente);
        criada.IdExterno.ShouldBe("sessao-1");
        formatura.Status.ShouldBe(StatusDaFormatura.AguardandoPagamento);
        await _unitOfWork.Received(1).SalvarAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>R$ 349,90 sai do catálogo e chega ao provedor como 34990, sem passar por decimal.</summary>
    [Fact]
    public async Task Valor_chega_ao_provedor_em_centavos_exatos()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Rascunho);

        await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("completo"), Ct);

        await _provedor
            .Received(1)
            .CriarCheckout(
                Arg.Is<PedidoDeCheckout>(p => p.PrecoEmCentavos == 34990 && p.UrlDeRetorno == "https://app.kapa/assinatura/retorno"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Checkout_de_formatura_ativa_devolve_ja_ativa_sem_chamar_o_provedor()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Ativa);

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("completo"), Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("assinatura.ja_ativa");
        await _provedor.DidNotReceiveWithAnyArgs().CriarCheckout(default!, Ct);
    }

    /// <summary>Provedor fora do ar: 503, nada gravado, e a formatura continua em rascunho.</summary>
    [Fact]
    public async Task Provedor_fora_do_ar_nao_grava_nada()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Rascunho);
        _provedor
            .CriarCheckout(Arg.Any<PedidoDeCheckout>(), Arg.Any<CancellationToken>())
            .Returns(Result.Falha<SessaoDeCheckout>(Erro.Indisponivel("assinatura.provedor_fora_do_ar", "Fora do ar.")));

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("completo"), Ct);

        resultado.Erros.ShouldHaveSingleItem().Tipo.ShouldBe(ETipoErro.Indisponivel);
        formatura.Status.ShouldBe(StatusDaFormatura.Rascunho);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
        await _assinaturas.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }

    /// <summary>Fechou a aba e clicou de novo: retoma a pendente em vez de empilhar outra.</summary>
    [Fact]
    public async Task Checkout_repetido_retoma_a_pendente()
    {
        var formatura = FormaturaEm(StatusDaFormatura.AguardandoPagamento);
        var pendente = new Assinatura();
        _assinaturas.ObterMaisRecenteParaEdicao(Arg.Any<CancellationToken>()).Returns(pendente);

        await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("completo"), Ct);

        await _assinaturas.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
        await _provedor.Received(1).CriarCheckout(Arg.Is<PedidoDeCheckout>(p => p.AssinaturaId == pendente.Id), Arg.Any<CancellationToken>());
        pendente.IdExterno.ShouldBe("sessao-1");
    }

    /// <summary>
    /// Pediu o Essencial, depois o Completo: a sessão do Essencial morre no provedor antes da troca,
    /// senão pagá-la ativaria o Completo pelo preço do Essencial.
    /// </summary>
    [Fact]
    public async Task Trocar_de_plano_invalida_a_sessao_anterior_no_provedor()
    {
        var formatura = FormaturaEm(StatusDaFormatura.AguardandoPagamento);
        var pendente = new Assinatura { PlanoId = Guid.CreateVersion7(), IdExterno = "sessao-essencial" };
        _assinaturas.ObterMaisRecenteParaEdicao(Arg.Any<CancellationToken>()).Returns(pendente);
        _provedor.Cancelar("sessao-essencial", Arg.Any<CancellationToken>()).Returns(Result.Ok());

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("completo"), Ct);

        resultado.Sucesso.ShouldBeTrue();
        await _provedor.Received(1).Cancelar("sessao-essencial", Arg.Any<CancellationToken>());
        pendente.PlanoId.ShouldBe(Completo.Id);
    }

    /// <summary>Se o provedor não expirou a sessão antiga, o plano não troca.</summary>
    [Fact]
    public async Task Sessao_anterior_que_nao_expira_mantem_o_plano()
    {
        var formatura = FormaturaEm(StatusDaFormatura.AguardandoPagamento);
        var planoAnterior = Guid.CreateVersion7();
        var pendente = new Assinatura { PlanoId = planoAnterior, IdExterno = "sessao-essencial" };
        _assinaturas.ObterMaisRecenteParaEdicao(Arg.Any<CancellationToken>()).Returns(pendente);
        _provedor
            .Cancelar("sessao-essencial", Arg.Any<CancellationToken>())
            .Returns(Result.Falha(Erro.Indisponivel("assinatura.provedor_fora_do_ar", "Fora do ar.")));

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("completo"), Ct);

        resultado.Falhou.ShouldBeTrue();
        pendente.PlanoId.ShouldBe(planoAnterior);
        await _provedor.DidNotReceiveWithAnyArgs().CriarCheckout(default!, Ct);
    }

    [Fact]
    public async Task Mesmo_plano_nao_cancela_a_sessao_anterior()
    {
        var formatura = FormaturaEm(StatusDaFormatura.AguardandoPagamento);
        _assinaturas
            .ObterMaisRecenteParaEdicao(Arg.Any<CancellationToken>())
            .Returns(new Assinatura { PlanoId = Completo.Id, IdExterno = "sessao-anterior" });

        await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("completo"), Ct);

        await _provedor.DidNotReceiveWithAnyArgs().Cancelar(default!, Ct);
    }

    /// <summary>Suspensa contrata, mas continua suspensa até o pagamento confirmar.</summary>
    [Fact]
    public async Task Checkout_de_suspensa_nao_muda_o_status_da_formatura()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Suspensa);

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("completo"), Ct);

        resultado.Sucesso.ShouldBeTrue();
        formatura.Status.ShouldBe(StatusDaFormatura.Suspensa);
    }

    [Fact]
    public async Task Plano_inexistente_devolve_validacao_no_campo()
    {
        var formatura = FormaturaEm(StatusDaFormatura.Rascunho);

        var resultado = await Servico.IniciarCheckout(formatura.Id, new IniciarCheckout("ouro"), Ct);

        resultado.Erros.ShouldHaveSingleItem().Campo.ShouldBe("planoCodigo");
    }

    [Fact]
    public async Task Cancelar_sem_assinatura_ativa_nao_chama_o_provedor()
    {
        _assinaturas.ObterMaisRecenteParaEdicao(Arg.Any<CancellationToken>()).Returns(new Assinatura { IdExterno = "sub-1" });

        var resultado = await Servico.Cancelar(Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("assinatura.nao_ativa");
        await _provedor.DidNotReceiveWithAnyArgs().Cancelar(default!, Ct);
    }
}
