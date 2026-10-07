using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Emails.Models;
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
/// As decisões da régua que não dependem do banco: a janela, a idempotência, o agrupamento por
/// pessoa, a régua padrão e o contato que o canal recusou.
/// </summary>
/// <remarks>
/// A barreira "não cobra quem tem informe pendente" não está aqui: ela mora na consulta, e é o teste
/// de integração que a exercita contra o Postgres. Testá-la com repositório substituído seria testar
/// o <c>Returns</c> do próprio teste.
/// </remarks>
public sealed class ReguaServiceTests
{
    /// <summary>Terça-feira, 15/09/2026, às 14h de Brasília.</summary>
    private static readonly DateTime DentroDaJanela = new(2026, 9, 15, 17, 0, 0, DateTimeKind.Utc);

    private static readonly DateOnly Hoje = new(2026, 9, 15);

    private static readonly FormaturaParaRegua Formatura = new(Guid.CreateVersion7(), "Medicina 2027");

    private static readonly Guid RegraDeTresDias = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly INotificacaoRepository _notificacoes = Substitute.For<INotificacaoRepository>();
    private readonly IParcelaRepository _parcelas = Substitute.For<IParcelaRepository>();
    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly ICanalDeNotificacao _canal = Substitute.For<ICanalDeNotificacao>();
    private readonly IAssinaturaRepository _assinaturas = Substitute.For<IAssinaturaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    public ReguaServiceTests()
    {
        _canal
            .Enviar(Arg.Any<MensagemDeNotificacao>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(new EntregaDaMensagem(Guid.CreateVersion7())));

        _notificacoes.ListarRegras(Arg.Any<CancellationToken>()).Returns([Degrau(3)]);
        _notificacoes.ListarChavesDoDia(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _notificacoes.ListarDestinatariosInvalidos(Arg.Any<CancellationToken>()).Returns([]);
        _notificacoes.ListarEntregasAConferir(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        _notificacoes.ListarParaCobranca(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _vinculos.ListarEmailsDaTesouraria(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _parcelas
            .ObterRegrasDeAtraso(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, RegrasDeAtraso>());
    }

    private ReguaService Servico =>
        new(
            _notificacoes,
            _parcelas,
            _vinculos,
            _canal,
            _assinaturas,
            Options.Create(new AplicacaoSettings { Nome = "Kapa", UrlDoFrontend = "https://kapa.dev" }),
            _unitOfWork,
            NullLogger<ReguaService>.Instance
        );

    private static RegraResumo Degrau(int dias, bool ativa = true) =>
        new(dias == 3 ? RegraDeTresDias : Guid.CreateVersion7(), GatilhoDaRegua.Vencimento, dias, ativa);

    private static ParcelaParaCobranca Parcela(Guid vinculoId, int diaDoVencimento, string descricao) =>
        new(Guid.CreateVersion7(), vinculoId, "Júlia Prado", "julia@turma.dev", new DateOnly(2026, 9, diaDoVencimento), 35_000, descricao);

    /// <summary>D+3 procura o vencimento de três dias atrás, no dia de Brasília.</summary>
    private void ComParcelas(params ParcelaParaCobranca[] parcelas) =>
        _notificacoes.ListarParaCobranca(new DateOnly(2026, 9, 12), Arg.Any<CancellationToken>()).Returns(parcelas);

    private IReadOnlyList<NotificacaoEnviada> EnviosGravados() =>
        [
            .. _notificacoes
                .ReceivedCalls()
                .Where(chamada => chamada.GetMethodInfo().Name == nameof(INotificacaoRepository.AdicionarEnvios))
                .SelectMany(chamada => (IReadOnlyList<NotificacaoEnviada>)chamada.GetArguments()[0]!),
        ];

    /// <summary>A régua é do módulo Avisos: turma sem ele no plano não entra na rodada.</summary>
    [Fact]
    public async Task So_a_turma_com_avisos_no_plano_entra_na_regua()
    {
        var premium = new FormaturaParaRegua(Guid.CreateVersion7(), "Premium");
        var essencial = new FormaturaParaRegua(Guid.CreateVersion7(), "Essencial");
        _notificacoes.ListarFormaturasAtivasDeTodasAsFormaturas(Arg.Any<CancellationToken>()).Returns([premium, essencial]);
        _assinaturas
            .ObterPlanoVigenteDeTodasAsFormaturas(premium.Id, Arg.Any<CancellationToken>())
            .Returns(new Plano { Modulos = [Modulo.Cobrancas, Modulo.Avisos] });
        _assinaturas
            .ObterPlanoVigenteDeTodasAsFormaturas(essencial.Id, Arg.Any<CancellationToken>())
            .Returns(new Plano { Modulos = [Modulo.Cobrancas] });

        var formaturas = await Servico.ListarFormaturas(Ct);

        formaturas.ShouldHaveSingleItem().ShouldBe(premium);
    }

    [Fact]
    public async Task Fora_da_janela_a_rodada_nao_toca_em_nada()
    {
        ComParcelas(Parcela(Guid.CreateVersion7(), 12, "Mensalidade 3/24"));

        var resumo = await Servico.Executar(Formatura, new DateTime(2026, 9, 15, 3, 0, 0, DateTimeKind.Utc), Ct);

        resumo.ShouldBe(ResumoDaRodada.Nenhuma);
        await _canal.DidNotReceiveWithAnyArgs().Enviar(default!, Ct);
        await _notificacoes.DidNotReceiveWithAnyArgs().AdicionarEnvios(default!, Ct);
    }

    /// <summary>Critério de aceite: três parcelas vencidas do mesmo formando geram um e-mail com as três.</summary>
    [Fact]
    public async Task Tres_parcelas_do_mesmo_formando_viram_uma_mensagem_com_as_tres()
    {
        var vinculoId = Guid.CreateVersion7();
        ComParcelas(Parcela(vinculoId, 12, "Mensalidade 1"), Parcela(vinculoId, 12, "Mensalidade 2"), Parcela(vinculoId, 12, "Rifa"));

        var resumo = await Servico.Executar(Formatura, DentroDaJanela, Ct);

        resumo.Parcelas.ShouldBe(3);
        resumo.Mensagens.ShouldBe(3);

        var enviadas = _canal.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ICanalDeNotificacao.Enviar)).ToList();
        enviadas.Count.ShouldBe(1);

        var mensagem = (MensagemDeNotificacao)enviadas[0].GetArguments()[0]!;
        mensagem.Para.ShouldBe("julia@turma.dev");
        mensagem.CorpoHtml.ShouldContain("Mensalidade 1");
        mensagem.CorpoHtml.ShouldContain("Mensalidade 2");
        mensagem.CorpoHtml.ShouldContain("Rifa");

        EnviosGravados().Count.ShouldBe(3);
    }

    /// <summary>Critério de aceite: rodar o job duas vezes no mesmo dia envia uma mensagem por pessoa.</summary>
    [Fact]
    public async Task A_segunda_rodada_do_dia_nao_manda_de_novo()
    {
        var parcela = Parcela(Guid.CreateVersion7(), 12, "Mensalidade 3/24");
        ComParcelas(parcela);

        await Servico.Executar(Formatura, DentroDaJanela, Ct);

        _notificacoes
            .ListarChavesDoDia(Hoje, Arg.Any<CancellationToken>())
            .Returns([.. EnviosGravados().Select(e => new ChaveDeEnvio(e.RegraId, e.ParcelaId))]);

        var segunda = await Servico.Executar(Formatura, DentroDaJanela, Ct);

        segunda.Mensagens.ShouldBe(0);
        await _canal.Received(1).Enviar(Arg.Any<MensagemDeNotificacao>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Critério de aceite: falha permanente marca o contato como inválido e para de tentar.</summary>
    [Fact]
    public async Task O_endereco_que_o_canal_recusou_nao_e_tentado_de_novo()
    {
        var notificacao = NotificacaoEnviada.Nova(RegraDeTresDias, Hoje, "julia@turma.dev", "Parcela em atraso");
        _notificacoes
            .ListarEntregasAConferir(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new EntregaAConferir(notificacao, EEmailStatus.Falhou, "550 mailbox unavailable")]);
        _notificacoes.ListarDestinatariosInvalidos(Arg.Any<CancellationToken>()).Returns(["julia@turma.dev"]);
        ComParcelas(Parcela(Guid.CreateVersion7(), 12, "Mensalidade 3/24"));

        var resumo = await Servico.Executar(Formatura, DentroDaJanela, Ct);

        notificacao.Status.ShouldBe(StatusDaNotificacao.Falhou);
        notificacao.Erro.ShouldBe("550 mailbox unavailable");
        resumo.Conferidas.ShouldBe(1);
        resumo.Mensagens.ShouldBe(0);
        await _canal.DidNotReceiveWithAnyArgs().Enviar(default!, Ct);
    }

    /// <summary>Critério de aceite: turma nova recebe a régua padrão sem configurar nada.</summary>
    [Fact]
    public async Task Turma_sem_regua_recebe_a_padrao_gravada()
    {
        _notificacoes.ListarRegras(Arg.Any<CancellationToken>()).Returns([], [Degrau(3)]);

        await Servico.Executar(Formatura, DentroDaJanela, Ct);

        await _notificacoes
            .Received(1)
            .AdicionarRegras(Arg.Is<IReadOnlyList<RegraDeNotificacao>>(regras => regras.Count == ReguaDoKapa.Degraus.Count), Ct);
    }

    [Fact]
    public async Task Degrau_desligado_nao_dispara()
    {
        _notificacoes.ListarRegras(Arg.Any<CancellationToken>()).Returns([Degrau(3, ativa: false)]);
        ComParcelas(Parcela(Guid.CreateVersion7(), 12, "Mensalidade 3/24"));

        (await Servico.Executar(Formatura, DentroDaJanela, Ct)).Mensagens.ShouldBe(0);
        await _canal.DidNotReceiveWithAnyArgs().Enviar(default!, Ct);
    }

    /// <summary>O D+30 manda um resumo — um por membro da tesouraria —, e não uma cópia por parcela.</summary>
    [Fact]
    public async Task O_d30_avisa_a_tesouraria_com_um_resumo()
    {
        _notificacoes.ListarRegras(Arg.Any<CancellationToken>()).Returns([Degrau(30)]);
        _vinculos.ListarEmailsDaTesouraria(Formatura.Id, Arg.Any<CancellationToken>()).Returns(["tesouraria@turma.dev"]);
        _notificacoes
            .ListarParaCobranca(new DateOnly(2026, 8, 16), Arg.Any<CancellationToken>())
            .Returns([Parcela(Guid.CreateVersion7(), 16, "Mensalidade 1"), Parcela(Guid.CreateVersion7(), 16, "Mensalidade 2")]);

        await Servico.Executar(Formatura, DentroDaJanela, Ct);

        var paraTesouraria = _canal
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ICanalDeNotificacao.Enviar))
            .Select(c => (MensagemDeNotificacao)c.GetArguments()[0]!)
            .Where(m => m.Para == "tesouraria@turma.dev")
            .ToList();

        paraTesouraria.ShouldHaveSingleItem().Assunto.ShouldBe("Parcelas com 30 dias de atraso");
        EnviosGravados().Count(e => e.ParcelaId is null).ShouldBe(1);
    }

    /// <summary>
    /// O resumo da conferência sai no dia em que um aviso completa três dias parado, e não em todo dia
    /// seguinte: os que já estavam parados ontem não disparam de novo.
    /// </summary>
    [Theory]
    [InlineData(4, 3, true)]
    [InlineData(3, 3, false)]
    public async Task O_resumo_da_conferencia_sai_so_quando_um_aviso_cruza_o_prazo(int parados, int jaParadosOntem, bool sai)
    {
        _notificacoes
            .ListarRegras(Arg.Any<CancellationToken>())
            .Returns([new RegraResumo(Guid.CreateVersion7(), GatilhoDaRegua.InformePendente, 3, true)]);
        _vinculos.ListarEmailsDaTesouraria(Formatura.Id, Arg.Any<CancellationToken>()).Returns(["tesouraria@turma.dev"]);
        _notificacoes.ContarInformesPendentesAte(new DateOnly(2026, 9, 12), Arg.Any<CancellationToken>()).Returns(parados);
        _notificacoes.ContarInformesPendentesAte(new DateOnly(2026, 9, 11), Arg.Any<CancellationToken>()).Returns(jaParadosOntem);

        await Servico.Executar(Formatura, DentroDaJanela, Ct);

        EnviosGravados().Count.ShouldBe(sai ? 1 : 0);
    }

    /// <summary>
    /// Mensagem recusada pelo canal não vira envio gravado: o histórico não mente sobre o que saiu.
    /// </summary>
    [Fact]
    public async Task Mensagem_recusada_pelo_canal_nao_grava_envio()
    {
        _canal
            .Enviar(Arg.Any<MensagemDeNotificacao>(), Arg.Any<CancellationToken>())
            .Returns(Result.Falha<EntregaDaMensagem>(Erro.Conflito("email.recusado", "O provedor recusou.")));
        ComParcelas(Parcela(Guid.CreateVersion7(), 12, "Mensalidade 3/24"));

        (await Servico.Executar(Formatura, DentroDaJanela, Ct)).Mensagens.ShouldBe(0);
        EnviosGravados().ShouldBeEmpty();
    }
}
