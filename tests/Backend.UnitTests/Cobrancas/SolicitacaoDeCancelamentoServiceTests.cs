using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Services;
using Backend.Business.Cobrancas.Validators;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Services;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Loja.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Pagamentos.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Cobrancas;

/// <summary>
/// A solicitação de cancelamento do pacote da cesta (Sprint 48): aprovar tira tudo, recusar devolve a cobrança.
/// </summary>
/// <remarks>O pedido avulso passa pelo <c>PedidoService</c>, coberto em <see cref="PedidoServiceTests"/>.</remarks>
public sealed class SolicitacaoDeCancelamentoServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid FormaturaId = Guid.CreateVersion7();

    private static readonly Guid UsuarioId = Guid.CreateVersion7();

    private static readonly Guid VinculoId = Guid.CreateVersion7();

    private readonly ISolicitacaoDeCancelamentoRepository _solicitacoes = Substitute.For<ISolicitacaoDeCancelamentoRepository>();

    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();

    private readonly IPlanoDeCobrancaRepository _planos = Substitute.For<IPlanoDeCobrancaRepository>();

    private readonly IParcelaRepository _parcelas = Substitute.For<IParcelaRepository>();

    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();

    private readonly IValorADevolverRepository _aDevolver = Substitute.For<IValorADevolverRepository>();

    private readonly IConviteDoEventoRepository _convites = Substitute.For<IConviteDoEventoRepository>();

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly PlanoDeCobranca _plano = new() { Nome = "Plano 2027" };

    private readonly ItemDeCobranca _fotos;

    public SolicitacaoDeCancelamentoServiceTests()
    {
        _fotos = ItemDeCobranca.NovoPacote(
            _plano.Id,
            new DadosDoPacote(new DadosDoItem(TipoDeCobranca.FotoEAlbum, "Fotos", 60_000, 3, 10, DataUtils.Hoje().AddMonths(-1)))
        );
        _plano.Itens.Add(_fotos);

        _perfis
            .ObterMembro(FormaturaId, UsuarioId, Arg.Any<CancellationToken>())
            .Returns(new MembroDoPerfil(VinculoId, UsuarioId, "Ana Souza", "ana@turma.dev", PapelNaFormatura.Formando));
        _pedidos.TravarItem(_fotos.Id, Arg.Any<CancellationToken>()).Returns(_fotos);
        _planos.ObterVigente(Arg.Any<CancellationToken>()).Returns(_plano);
        _planos.ListarCesta(VinculoId, Arg.Any<CancellationToken>()).Returns([]);
        _convites.TravarDosPacotes(default, default).ReturnsForAnyArgs([]);
        _convites.ListarComEntrada(default!, default).ReturnsForAnyArgs(new HashSet<Guid>());
        _solicitacoes.Obter(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(chamada => Resumo(chamada.Arg<Guid>()));

        // A transação de verdade é do Postgres; aqui ela é o passa-adiante que deixa o service rodar.
        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<Result>>>()(Ct));
    }

    private SolicitacaoDeCancelamentoService Servico =>
        new(
            _solicitacoes,
            _pedidos,
            _planos,
            _parcelas,
            _perfis,
            Substitute.For<IPedidoService>(),
            new AberturaDeSolicitacao(_solicitacoes, _parcelas, Substitute.For<IEventoRepository>()),
            new EmissaoDeConvites(
                _convites,
                Substitute.For<IEventoDaTurmaRepository>(),
                Substitute.For<IFormaturaRepository>(),
                Substitute.For<IFormaturaAtual>(),
                NullLogger<EmissaoDeConvites>.Instance
            ),
            new ValoresADevolver(_aDevolver),
            new DadosDaSolicitacaoValidator(),
            Substitute.For<IEventoRepository>(),
            _unitOfWork,
            NullLogger<SolicitacaoDeCancelamentoService>.Instance
        );

    private static ResumoDaSolicitacao Resumo(Guid id) =>
        new(
            id,
            UsuarioId,
            "Ana Souza",
            Guid.Empty,
            TipoDeCobranca.FotoEAlbum,
            "Fotos",
            null,
            null,
            null,
            DateTime.UtcNow,
            DataUtils.Hoje(),
            StatusDoPedidoDeCancelamento.Aberto,
            null,
            null,
            0
        );

    private SolicitacaoDeCancelamento Aberta()
    {
        var solicitacao = new SolicitacaoDeCancelamento(VinculoId, _fotos.Id, null, "não quero mais", DateTime.UtcNow, DataUtils.Hoje());
        _solicitacoes.Travar(solicitacao.Id, Arg.Any<CancellationToken>()).Returns(solicitacao);

        return solicitacao;
    }

    /// <summary>
    /// D8/D9: aprovar o pacote cancela todas as parcelas dele — a paga inclusive —, leva o pago à lista "a devolver" e
    /// tira o pacote da cesta.
    /// </summary>
    [Fact]
    public async Task Aprovar_o_pacote_desfaz_as_parcelas_e_leva_o_pago_a_devolver()
    {
        // Arrange
        var solicitacao = Aberta();
        var hoje = DataUtils.Hoje();
        var paga = Parcela.Nova(VinculoId, _fotos.Id, new ParcelaPrevista(1, hoje.AddMonths(-1), 20_000));
        paga.Pagar(20_000, hoje.AddMonths(-1), 20_000);
        var futura = Parcela.Nova(VinculoId, _fotos.Id, new ParcelaPrevista(2, hoje.AddMonths(1), 20_000));
        futura.Suspender(solicitacao.RespostaAte);
        _parcelas.ListarDoVinculoNoItemParaEdicao(VinculoId, _fotos.Id, Arg.Any<CancellationToken>()).Returns([paga, futura]);
        var escolha = EscolhaDaCesta.Nova(VinculoId, _fotos.Id);
        _planos.ObterEscolhaParaEdicao(VinculoId, _fotos.Id, Arg.Any<CancellationToken>()).Returns(escolha);

        // Act
        var resultado = await Servico.Aprovar(FormaturaId, solicitacao.Id, UsuarioId, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        solicitacao.Status.ShouldBe(StatusDoPedidoDeCancelamento.Aprovado);
        paga.Status.ShouldBe(StatusDaParcela.Cancelada);
        futura.Status.ShouldBe(StatusDaParcela.Cancelada);
        futura.SuspensaAte.ShouldBeNull();
        _planos.Received(1).RemoverEscolha(escolha);
        await _aDevolver.Received(1).Adicionar(Arg.Is<ValorADevolver>(valor => valor.ValorEmCentavos == 20_000), Arg.Any<CancellationToken>());
    }

    /// <summary>Recusar devolve a cobrança no mesmo dia (D12) e exige o motivo, que o formando lê.</summary>
    [Fact]
    public async Task Recusar_retoma_a_cobranca()
    {
        // Arrange
        var solicitacao = Aberta();
        var futura = Parcela.Nova(VinculoId, _fotos.Id, new ParcelaPrevista(2, DataUtils.Hoje().AddMonths(1), 20_000));
        futura.Suspender(solicitacao.RespostaAte);
        _parcelas.ListarDoVinculoNoItemParaEdicao(VinculoId, _fotos.Id, Arg.Any<CancellationToken>()).Returns([futura]);

        // Act
        var semMotivo = await Servico.Recusar(solicitacao.Id, " ", UsuarioId, Ct);
        var resultado = await Servico.Recusar(solicitacao.Id, "o fotógrafo já foi contratado", UsuarioId, Ct);

        // Assert
        semMotivo.PrimeiroErro.Codigo.ShouldBe("cobranca.motivo_da_recusa");
        resultado.Sucesso.ShouldBeTrue();
        solicitacao.Status.ShouldBe(StatusDoPedidoDeCancelamento.Recusado);
        solicitacao.MotivoDaResposta.ShouldBe("o fotógrafo já foi contratado");
        futura.SuspensaAte.ShouldBeNull();
        futura.Status.ShouldBe(StatusDaParcela.Aberta);
    }

    /// <summary>Rateio e lançamento avulso não são do formando: só pacote da cesta ou pedido se cancelam por aqui.</summary>
    [Fact]
    public async Task Pacote_fora_da_cesta_nao_abre_solicitacao()
    {
        // Act
        var resultado = await Servico.Solicitar(FormaturaId, UsuarioId, new DadosDaSolicitacao(_fotos.Id, null), Ct);

        // Assert
        resultado.PrimeiroErro.Codigo.ShouldBe("cobranca.nada_a_cancelar");
        await _solicitacoes.DidNotReceiveWithAnyArgs().Adicionar(default!, Ct);
    }
}
