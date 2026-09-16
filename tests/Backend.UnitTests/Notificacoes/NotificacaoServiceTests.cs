using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;
using Backend.Business.Notificacoes.Services;
using Backend.Business.Notificacoes.Validators;
using Backend.Business.Usuarios.Interfaces;
using Backend.Business.Usuarios.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Notificacoes;

/// <summary>
/// As regras da tela da régua: o teste vai só para quem clicou, a cobrança não se desliga e a
/// gravação mantém o par (gatilho, deslocamento) como identidade do degrau.
/// </summary>
public sealed class NotificacaoServiceTests
{
    private static readonly Guid FormaturaId = Guid.CreateVersion7();
    private static readonly Guid UsuarioId = Guid.CreateVersion7();
    private static readonly Guid VinculoId = Guid.CreateVersion7();
    private static readonly Guid RegraId = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly INotificacaoRepository _notificacoes = Substitute.For<INotificacaoRepository>();
    private readonly IParcelaRepository _parcelas = Substitute.For<IParcelaRepository>();
    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();
    private readonly IUsuarioRepository _usuarios = Substitute.For<IUsuarioRepository>();
    private readonly ICanalDeNotificacao _canal = Substitute.For<ICanalDeNotificacao>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public NotificacaoServiceTests()
    {
        _canal.Canal.Returns(CanalDeNotificacao.Email);
        _canal
            .Enviar(Arg.Any<MensagemDeNotificacao>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new EntregaDaMensagem(Guid.CreateVersion7())));

        _notificacoes.ListarRegras(Arg.Any<CancellationToken>()).Returns([Degrau()]);
        _notificacoes.ListarRegrasParaEdicao(Arg.Any<CancellationToken>()).Returns([]);
        _notificacoes.ListarPreferenciasParaEdicao(VinculoId, Arg.Any<CancellationToken>()).Returns([]);

        _usuarios
            .ObterDetalhe(UsuarioId, Arg.Any<CancellationToken>())
            .Returns(new UsuarioDetalhe(UsuarioId, "Ana Tesoureira", "ana@turma.dev", true, true, [], DateTime.UtcNow, DateTime.UtcNow));

        _vinculos.ObterAtivoParaEdicao(UsuarioId, FormaturaId, Arg.Any<CancellationToken>()).Returns(Vinculo());

        _formaturas
            .ObterDetalhe(FormaturaId, Arg.Any<CancellationToken>())
            .Returns(
                new FormaturaDetalhe(
                    FormaturaId,
                    "Medicina 2027",
                    "UFPR",
                    "Medicina",
                    2027,
                    1,
                    null,
                    null,
                    80,
                    StatusDaFormatura.Ativa,
                    DateTime.UtcNow,
                    DateTime.UtcNow,
                    null
                )
            );
    }

    private NotificacaoService Servico =>
        new(
            _notificacoes,
            _parcelas,
            _vinculos,
            _formaturas,
            _usuarios,
            [_canal],
            new DadosDaReguaValidator(),
            new DadosDasPreferenciasValidator(),
            Options.Create(new AplicacaoSettings { Nome = "Kapa", UrlDoFrontend = "https://kapa.dev" }),
            _unitOfWork,
            NullLogger<NotificacaoService>.Instance
        );

    private static VinculoDeFormatura Vinculo()
    {
        var vinculo = new VinculoDeFormatura
        {
            UsuarioId = UsuarioId,
            FormaturaId = FormaturaId,
            Papel = PapelNaFormatura.Tesoureiro,
        };

        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(vinculo, VinculoId);

        return vinculo;
    }

    private static RegraResumo Degrau() =>
        new(
            RegraId,
            GatilhoDaRegua.Vencimento,
            3,
            CanalDeNotificacao.Email,
            "Parcela em atraso — {formatura}",
            "Oi, {nome}. São {valor}.",
            true,
            false
        );

    /// <summary>Critério de aceite: <c>testar</c> envia só para quem clicou.</summary>
    [Fact]
    public async Task Testar_manda_so_para_quem_clicou()
    {
        var resultado = await Servico.Testar(FormaturaId, UsuarioId, RegraId, Ct);

        resultado.Sucesso.ShouldBeTrue();
        await _canal.Received(1).Enviar(Arg.Is<MensagemDeNotificacao>(m => m.Para == "ana@turma.dev"), Arg.Any<CancellationToken>());
        await _notificacoes.DidNotReceiveWithAnyArgs().AdicionarEnvios(default!, Ct);
    }

    [Fact]
    public async Task Testar_um_degrau_que_nao_existe_devolve_404()
    {
        var resultado = await Servico.Testar(FormaturaId, UsuarioId, Guid.CreateVersion7(), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.NaoEncontrado);
    }

    /// <summary>Critério de aceite: a notificação de cobrança não pode ser desativada pelo formando.</summary>
    [Fact]
    public async Task Desligar_a_cobranca_devolve_409()
    {
        var resultado = await Servico.SalvarPreferencias(
            FormaturaId,
            UsuarioId,
            new DadosDasPreferencias([new PreferenciaEscolhida(TipoDeNotificacao.Cobranca, false)]),
            Ct
        );

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("notificacao.cobranca_obrigatoria");
        resultado.PrimeiroErro.Tipo.ShouldBe(ETipoErro.Conflito);
        await _notificacoes.DidNotReceiveWithAnyArgs().AdicionarPreferencias(default!, Ct);
    }

    /// <summary>...e o aviso do mural, sim.</summary>
    [Fact]
    public async Task Desligar_o_aviso_do_mural_grava()
    {
        var resultado = await Servico.SalvarPreferencias(
            FormaturaId,
            UsuarioId,
            new DadosDasPreferencias([new PreferenciaEscolhida(TipoDeNotificacao.Aviso, false)]),
            Ct
        );

        resultado.Sucesso.ShouldBeTrue();
        resultado.Valor.ShouldContain(p => p.Tipo == TipoDeNotificacao.Aviso && !p.Ativa);
        resultado.Valor.ShouldContain(p => p.Tipo == TipoDeNotificacao.Cobranca && p.Ativa && p.Obrigatoria);
        await _notificacoes
            .Received(1)
            .AdicionarPreferencias(Arg.Is<IReadOnlyList<PreferenciaDeNotificacao>>(p => p.Count == 1 && p[0].Tipo == TipoDeNotificacao.Aviso), Ct);
    }

    [Fact]
    public async Task A_gravacao_da_regua_atualiza_o_degrau_que_ja_existe_em_vez_de_duplicar()
    {
        var gravada = RegraDeNotificacao.Nova(GatilhoDaRegua.Vencimento, 3, "Antigo", "Texto antigo");
        _notificacoes.ListarRegrasParaEdicao(Arg.Any<CancellationToken>()).Returns([gravada]);

        var resultado = await Servico.SalvarRegras(
            new DadosDaRegua([new DadosDaRegra(GatilhoDaRegua.Vencimento, 3, CanalDeNotificacao.Email, "Novo", "Oi, {nome}.", true, false)]),
            Ct
        );

        resultado.Sucesso.ShouldBeTrue();
        gravada.Assunto.ShouldBe("Novo");
        gravada.Template.ShouldBe("Oi, {nome}.");
        await _notificacoes.Received().AdicionarRegras(Arg.Is<IReadOnlyList<RegraDeNotificacao>>(r => r.Count == 0), Ct);
    }

    [Fact]
    public async Task Cobrar_uma_parcela_que_a_regua_nao_pode_cobrar_devolve_409()
    {
        _notificacoes.ObterParaCobranca(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ParcelaParaCobranca?)null);

        var resultado = await Servico.Cobrar(FormaturaId, Guid.CreateVersion7(), Ct);

        resultado.Falhou.ShouldBeTrue();
        resultado.PrimeiroErro.Codigo.ShouldBe("notificacao.parcela_nao_cobravel");
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
