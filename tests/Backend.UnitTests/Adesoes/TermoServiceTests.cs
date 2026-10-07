using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Business.Adesoes.Services;
using Backend.Business.Adesoes.Validators;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Formandos.Interfaces;
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
    private readonly IEventoRepository _eventos = Substitute.For<IEventoRepository>();
    private readonly IFormaturaAtual _formatura = Substitute.For<IFormaturaAtual>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    /// <summary>Plano em vigor por padrão: publicar o termo exige um (decisão de 06/10/2026).</summary>
    public TermoServiceTests() => _planos.ExisteVigente(Arg.Any<CancellationToken>()).Returns(true);

    private TermoService Servico =>
        new(
            _adesoes,
            _planos,
            Substitute.For<IPerfilRepository>(),
            new PublicarTermoValidator(),
            _eventos,
            _formatura,
            _unitOfWork,
            NullLogger<TermoService>.Instance
        );

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

    /// <summary>
    /// O evento leva a formatura <b>da sessão</b>, e não a do termo recém-criado.
    /// </summary>
    /// <remarks>
    /// Achado em 17/09/2026. Quem carimba <c>FormaturaId</c> na entidade é o <c>SaveChangesAsync</c>,
    /// que ainda não rodou quando o evento é montado: lê-la ali grava <c>Guid.Empty</c> no corpo, e
    /// o evento some da trilha da turma — que filtra justamente por essa coluna.
    /// </remarks>
    [Fact]
    public async Task Publicar_audita_com_a_formatura_da_sessao()
    {
        // Arrange
        var formaturaId = Guid.CreateVersion7();
        var autorId = Guid.CreateVersion7();
        _formatura.Id.Returns(formaturaId);
        _adesoes.ObterTermoVigente(Arg.Any<CancellationToken>()).Returns((VersaoDoTermo?)null);

        // Act
        await Servico.Publicar(autorId, new PublicarTermo("Primeira versão do termo."), Ct);

        // Assert
        await _eventos
            .Received(1)
            .Adicionar(
                Arg.Is<Evento>(evento =>
                    evento.Nome == NomesDeAuditoria.TermoPublicado && evento.UsuarioId == autorId && evento.FormaturaId == formaturaId
                ),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>O termo é assinado com o quadro de escolhas do plano: sem plano em vigor, não há o que assinar.</summary>
    [Fact]
    public async Task Publicar_sem_plano_em_vigor_devolve_conflito()
    {
        // Arrange
        _planos.ExisteVigente(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var resultado = await Servico.Publicar(Guid.CreateVersion7(), new PublicarTermo("Primeira versão do termo."), Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("adesao.termo_sem_plano_vigente");
        await _adesoes.DidNotReceiveWithAnyArgs().AdicionarTermo(default!, Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SalvarAsync(Ct);
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

        var conteudo = (await Servico.ObterParaAdesao(Guid.CreateVersion7(), Guid.CreateVersion7(), [], Ct)).Valor;

        conteudo.Termo.ShouldNotBeNull();
        conteudo.Plano.ShouldBeNull();
        conteudo.HashDoConteudo.ShouldBeNull();
    }
}
