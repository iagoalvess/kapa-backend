using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;
using Backend.Business.Notificacoes.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Notificacoes;

/// <summary>
/// As regras da tela da régua: o teste vai só para quem clicou e a gravação mantém o par (gatilho, deslocamento) como identidade do degrau.
/// </summary>
public sealed class NotificacaoServiceTests
{
    private static readonly Guid FormaturaId = Guid.CreateVersion7();
    private static readonly Guid VinculoId = Guid.CreateVersion7();
    private static readonly Guid RegraId = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly INotificacaoRepository _notificacoes = Substitute.For<INotificacaoRepository>();
    private readonly IParcelaRepository _parcelas = Substitute.For<IParcelaRepository>();
    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();
    private readonly ICanalDeNotificacao _canal = Substitute.For<ICanalDeNotificacao>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public NotificacaoServiceTests()
    {
        _canal
            .Enviar(Arg.Any<MensagemDeNotificacao>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new EntregaDaMensagem(Guid.CreateVersion7())));

        _notificacoes.ListarRegras(Arg.Any<CancellationToken>()).Returns([Degrau()]);

        _formaturas.ObterNome(FormaturaId, Arg.Any<CancellationToken>()).Returns("Medicina 2027");
    }

    private NotificacaoService Servico =>
        new(
            _notificacoes,
            _parcelas,
            _formaturas,
            _canal,
            Options.Create(new AplicacaoSettings { Nome = "Kapa", UrlDoFrontend = "https://kapa.dev" }),
            _unitOfWork,
            NullLogger<NotificacaoService>.Instance
        );

    private static RegraResumo Degrau() => new(RegraId, GatilhoDaRegua.Vencimento, 3, true);

    [Fact]
    public async Task Desligar_um_degrau_grava_so_a_situacao()
    {
        var gravada = RegraDeNotificacao.Nova(ReguaDoKapa.De(GatilhoDaRegua.Vencimento, 3)!);
        _notificacoes.ObterRegraParaEdicao(RegraId, Arg.Any<CancellationToken>()).Returns(gravada);

        var resultado = await Servico.DefinirRegra(RegraId, false, Ct);

        resultado.Sucesso.ShouldBeTrue();
        gravada.Ativa.ShouldBeFalse();
        await _unitOfWork.Received().SalvarAsync(Ct);
    }

    [Fact]
    public async Task Definir_um_degrau_que_nao_existe_devolve_404()
    {
        var resultado = await Servico.DefinirRegra(Guid.CreateVersion7(), false, Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.NaoEncontrado);
    }

    /// <summary>O degrau que a turma criou quando o texto era editável não volta a ser ligado.</summary>
    [Fact]
    public async Task Definir_um_degrau_que_saiu_da_regua_devolve_404()
    {
        var antiga = RegraDeNotificacao.Nova(new DegrauDaRegua(GatilhoDaRegua.Vencimento, 7, new("Antigo", "Texto antigo")));
        _notificacoes.ObterRegraParaEdicao(RegraId, Arg.Any<CancellationToken>()).Returns(antiga);

        var resultado = await Servico.DefinirRegra(RegraId, true, Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.NaoEncontrado);
    }

    [Fact]
    public async Task Cobrar_uma_parcela_que_a_regua_nao_pode_cobrar_devolve_409()
    {
        var parcelaId = Guid.CreateVersion7();
        _notificacoes.ObterParaCobranca(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ParcelaParaCobranca?)null);
        _parcelas
            .Obter(parcelaId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(
                new ParcelaResumo(
                    parcelaId,
                    VinculoId,
                    Guid.CreateVersion7(),
                    "Júlia",
                    Guid.CreateVersion7(),
                    TipoDeCobranca.Mensalidade,
                    null,
                    1,
                    3,
                    new DateOnly(2026, 9, 1),
                    35_000,
                    StatusDaParcela.Paga,
                    ValorPagoEmCentavos: 35_000
                )
            );

        var resultado = await Servico.Cobrar(FormaturaId, parcelaId, Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("notificacao.parcela_nao_cobravel");
    }

    /// <summary>Parcela de outra turma é a mesma coisa que parcela que não existe: 404, sem dizer qual dos dois.</summary>
    [Fact]
    public async Task Cobrar_uma_parcela_que_nao_e_da_turma_devolve_404()
    {
        _notificacoes.ObterParaCobranca(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ParcelaParaCobranca?)null);
        _parcelas.Obter(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns((ParcelaResumo?)null);

        var resultado = await Servico.Cobrar(FormaturaId, Guid.CreateVersion7(), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("pagamento.parcela_nao_encontrada");
    }

    [Fact]
    public async Task Cobrar_duas_vezes_no_mesmo_dia_devolve_409()
    {
        var parcelaId = Guid.CreateVersion7();
        _notificacoes
            .ObterParaCobranca(parcelaId, Arg.Any<CancellationToken>())
            .Returns(new ParcelaParaCobranca(parcelaId, VinculoId, "Júlia", "julia@turma.dev", new DateOnly(2026, 9, 1), 35_000, "Mensalidade"));
        _notificacoes.ListarChavesDoDia(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([new ChaveDeEnvio(RegraId, parcelaId)]);
        _parcelas
            .ObterRegrasDeAtraso(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, RegrasDeAtraso>());

        var resultado = await Servico.Cobrar(FormaturaId, parcelaId, Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("notificacao.ja_cobrada_hoje");
    }
}
