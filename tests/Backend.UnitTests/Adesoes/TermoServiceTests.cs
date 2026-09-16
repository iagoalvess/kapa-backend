using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Business.Adesoes.Services;
using Backend.Business.Adesoes.Validators;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Adesoes;

/// <summary>Publicar é inserir a versão seguinte; o conteúdo para adesão só tem hash com termo e plano.</summary>
public sealed class TermoServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IAdesaoRepository _adesoes = Substitute.For<IAdesaoRepository>();
    private readonly IPlanoDeCobrancaRepository _planos = Substitute.For<IPlanoDeCobrancaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private TermoService Servico => new(_adesoes, _planos, new PublicarTermoValidator(), _unitOfWork, NullLogger<TermoService>.Instance);

    [Fact]
    public async Task Publicar_grava_a_versao_seguinte()
    {
        _adesoes.ObterTermoVigente(Arg.Any<CancellationToken>()).Returns(new VersaoDoTermo(Guid.CreateVersion7(), 3, "v3", DateTime.UtcNow));

        var publicado = (await Servico.Publicar(Guid.CreateVersion7(), new PublicarTermo("  v4  "), Ct)).Valor;

        publicado.Versao.ShouldBe(4);
        publicado.Conteudo.ShouldBe("v4");
        await _adesoes.Received(1).AdicionarTermo(Arg.Is<TermoDaFormatura>(t => t.Versao == 4), Ct);
        await _unitOfWork.Received(1).SalvarAsync(Ct);
    }

    [Fact]
    public async Task Publicar_o_mesmo_texto_da_vigente_devolve_conflito()
    {
        _adesoes.ObterTermoVigente(Arg.Any<CancellationToken>()).Returns(new VersaoDoTermo(Guid.CreateVersion7(), 1, "Texto", DateTime.UtcNow));

        (await Servico.Publicar(Guid.CreateVersion7(), new PublicarTermo("Texto"), Ct)).PrimeiroErro.Codigo.ShouldBe("adesao.termo_sem_mudanca");
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Texto_vazio_devolve_validacao()
    {
        (await Servico.Publicar(Guid.CreateVersion7(), new PublicarTermo(" "), Ct)).PrimeiroErro.Tipo.ShouldBe(ETipoErro.Validacao);
    }

    [Fact]
    public async Task Sem_plano_vigente_o_conteudo_vem_sem_plano_e_sem_hash()
    {
        _adesoes.ObterTermoVigente(Arg.Any<CancellationToken>()).Returns(new VersaoDoTermo(Guid.CreateVersion7(), 1, "Texto", DateTime.UtcNow));
        _planos.ObterVigente(Arg.Any<CancellationToken>()).Returns((PlanoDeCobranca?)null);

        var conteudo = (await Servico.ObterParaAdesao(Ct)).Valor;

        conteudo.Termo.ShouldNotBeNull();
        conteudo.Plano.ShouldBeNull();
        conteudo.HashDoConteudo.ShouldBeNull();
    }
}
