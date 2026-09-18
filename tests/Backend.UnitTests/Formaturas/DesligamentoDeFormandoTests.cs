using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Formaturas.Services;
using Backend.Business.Formaturas.Validators;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Formaturas;

/// <summary>
/// A saída do formando: o que é cancelado, o que não é, e as portas que a recusam.
/// </summary>
/// <remarks>
/// Aqui ficam as regras que dependem do estado da turma. Quem confere se a régua e a adimplência
/// param de contar o desligado é a suíte de integração, sobre as consultas de verdade — o critério
/// de aceite pede teste sobre a consulta da régua, não sobre a tela.
/// </remarks>
public sealed class DesligamentoDeFormandoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid FormaturaId = Guid.CreateVersion7();
    private static readonly Guid UsuarioId = Guid.CreateVersion7();
    private static readonly Guid AutorId = Guid.CreateVersion7();

    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly IPerfilRepository _perfis = Substitute.For<IPerfilRepository>();
    private readonly IParcelaRepository _parcelas = Substitute.For<IParcelaRepository>();
    private readonly IAdesaoRepository _adesoes = Substitute.For<IAdesaoRepository>();
    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();
    private readonly IEventoRepository _eventos = Substitute.For<IEventoRepository>();
    private readonly IEmailService _emailService = Substitute.For<IEmailService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    /// <summary>O vínculo da turma, um por teste — o <c>Id</c> nasce com ele e não se atribui.</summary>
    private readonly VinculoDeFormatura _vinculo = new()
    {
        UsuarioId = UsuarioId,
        FormaturaId = FormaturaId,
        Papel = PapelNaFormatura.Formando,
    };

    private Guid VinculoId => _vinculo.Id;

    public DesligamentoDeFormandoTests()
    {
        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<Result>>>()(CancellationToken.None));

        _vinculos.TravarPresidentesAtivos(FormaturaId, Arg.Any<CancellationToken>()).Returns(2);
        _adesoes.JaAderiuAlgumaVez(VinculoId, Arg.Any<CancellationToken>()).Returns(true);
        _perfis
            .ObterMembro(FormaturaId, UsuarioId, Arg.Any<CancellationToken>())
            .Returns(new MembroDoPerfil(VinculoId, UsuarioId, "João Pedro", "joao@exemplo.com", PapelNaFormatura.Formando));
        _parcelas.ListarDoVinculo(VinculoId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private MembroService Servico =>
        new(
            _vinculos,
            _perfis,
            _parcelas,
            _adesoes,
            _formaturas,
            _eventos,
            new EmailsDeDesligamento(_emailService, Options.Create(new AplicacaoSettings())),
            new AlterarPapelValidator(),
            new DesligarFormandoValidator(),
            _unitOfWork
        );

    private static DesligarFormando Pedido(bool cancelarAtraso = false, string motivo = MotivoDeSaida.Trancamento, string? detalhe = null) =>
        new(motivo, detalhe, cancelarAtraso);

    /// <summary>Uma parcela do vínculo, com o vencimento relativo a hoje.</summary>
    /// <param name="diasAteVencer">Negativo para o que já venceu.</param>
    /// <param name="valorEmCentavos">Valor original.</param>
    private Parcela Parcela(int diasAteVencer, long valorEmCentavos = 45_000) =>
        Backend.Business.Cobrancas.Models.Parcela.Nova(
            VinculoId,
            Guid.CreateVersion7(),
            new ParcelaPrevista(1, DataUtils.Hoje().AddDays(diasAteVencer), valorEmCentavos)
        );

    private void ComVinculo(VinculoDeFormatura vinculo) =>
        _vinculos.ObterParaEdicao(UsuarioId, FormaturaId, Arg.Any<CancellationToken>()).Returns(vinculo);

    private void ComParcelas(params Parcela[] parcelas) =>
        _parcelas.ListarEmAbertoDoVinculoParaEdicao(VinculoId, Arg.Any<CancellationToken>()).Returns(parcelas);

    /// <summary>Cancela o futuro e poupa o atraso — o padrão, com a caixa desmarcada (P1 de 17/09/2026).</summary>
    [Fact]
    public async Task Desligar_cancela_o_que_nao_venceu_e_mantem_o_atraso_por_padrao()
    {
        // Arrange
        var vinculo = _vinculo;
        var futura = Parcela(30);
        var vencida = Parcela(-10);
        ComVinculo(vinculo);
        ComParcelas(futura, vencida);

        // Act
        var resultado = await Servico.Desligar(FormaturaId, UsuarioId, Pedido(), AutorId, Ct);

        // Assert
        resultado.Sucesso.ShouldBeTrue();
        futura.Status.ShouldBe(StatusDaParcela.Cancelada);
        vencida.Status.ShouldBe(StatusDaParcela.Aberta);
        vinculo.Ativo.ShouldBeFalse();
        vinculo.Desligado.ShouldBeTrue();
        vinculo.MotivoDoDesligamento.ShouldBe(MotivoDeSaida.Trancamento);
    }

    /// <summary>Com a caixa marcada, o atraso vai junto. Saiu, zerou.</summary>
    [Fact]
    public async Task Desligar_com_cancelar_atraso_cancela_tambem_o_vencido()
    {
        var futura = Parcela(30);
        var vencida = Parcela(-10);
        ComVinculo(_vinculo);
        ComParcelas(futura, vencida);

        var resultado = await Servico.Desligar(FormaturaId, UsuarioId, Pedido(cancelarAtraso: true), AutorId, Ct);

        resultado.Sucesso.ShouldBeTrue();
        futura.Status.ShouldBe(StatusDaParcela.Cancelada);
        vencida.Status.ShouldBe(StatusDaParcela.Cancelada);
    }

    /// <summary>
    /// Decisão 5: o que entrou, entrou. Parcela paga não vira cancelada, nem com o atraso marcado —
    /// senão o arrecadado e o balancete do mês passado mudariam de valor depois de emitidos.
    /// </summary>
    [Fact]
    public async Task Desligar_nao_toca_na_parcela_paga()
    {
        var paga = Parcela(-40);
        paga.Pagar(45_000, DataUtils.Hoje().AddDays(-40), 45_000);
        ComVinculo(_vinculo);
        ComParcelas(paga);

        await Servico.Desligar(FormaturaId, UsuarioId, Pedido(cancelarAtraso: true), AutorId, Ct);

        paga.Status.ShouldBe(StatusDaParcela.Paga);
    }

    /// <summary>Quem nunca aderiu não deve nada: a porta dele é Remover, e desligar não apaga dívida por engano.</summary>
    [Fact]
    public async Task Desligar_quem_nao_aderiu_devolve_membro_sem_adesao_sem_cancelar_nada()
    {
        var vinculo = _vinculo;
        var futura = Parcela(30);
        ComVinculo(vinculo);
        ComParcelas(futura);
        _adesoes.JaAderiuAlgumaVez(VinculoId, Arg.Any<CancellationToken>()).Returns(false);

        var resultado = await Servico.Desligar(FormaturaId, UsuarioId, Pedido(), AutorId, Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("formatura.membro_sem_adesao");
        futura.Status.ShouldBe(StatusDaParcela.Aberta);
        vinculo.Ativo.ShouldBeTrue();
    }

    /// <summary>Idempotente: o segundo desligamento não cancela mais nada e não manda o segundo e-mail.</summary>
    [Fact]
    public async Task Desligar_duas_vezes_nao_cancela_mais_nada_nem_manda_outro_email()
    {
        var vinculo = _vinculo;
        vinculo.Desligar(MotivoDeSaida.Trancamento, null, DateTime.UtcNow.AddDays(-1));
        var futura = Parcela(30);
        ComVinculo(vinculo);
        ComParcelas(futura);

        var resultado = await Servico.Desligar(FormaturaId, UsuarioId, Pedido(), AutorId, Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("formatura.membro_ja_desligado");
        futura.Status.ShouldBe(StatusDaParcela.Aberta);
        await _emailService.DidNotReceiveWithAnyArgs().Enfileirar(default!, Ct);
    }

    /// <summary>A mesma guarda de Remover: turma sem presidente é turma que ninguém administra.</summary>
    [Fact]
    public async Task Desligar_o_ultimo_presidente_e_recusado()
    {
        var vinculo = _vinculo;
        vinculo.Papel = PapelNaFormatura.Presidente;
        ComVinculo(vinculo);
        ComParcelas();
        _vinculos.TravarPresidentesAtivos(FormaturaId, Arg.Any<CancellationToken>()).Returns(1);

        var resultado = await Servico.Desligar(FormaturaId, UsuarioId, Pedido(), AutorId, Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("formatura.ultimo_presidente");
        vinculo.Ativo.ShouldBeTrue();
    }

    /// <summary>"Outro" sem justificativa é o silêncio de uma coluna vazia com outro nome.</summary>
    [Fact]
    public async Task Desligar_com_motivo_outro_exige_a_justificativa()
    {
        ComVinculo(_vinculo);
        ComParcelas();

        var resultado = await Servico.Desligar(FormaturaId, UsuarioId, Pedido(motivo: MotivoDeSaida.Outro), AutorId, Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("membro.detalhe_obrigatorio");
    }

    /// <summary>O evento leva motivo e valores: é o que responde "quanto sumiu da projeção" na assembleia.</summary>
    [Fact]
    public async Task Desligar_audita_com_motivo_e_o_que_foi_cancelado()
    {
        Evento? gravado = null;
        await _eventos.Adicionar(Arg.Do<Evento>(evento => gravado = evento), Arg.Any<CancellationToken>());
        ComVinculo(_vinculo);
        ComParcelas(Parcela(30, 60_000), Parcela(60, 60_000));

        await Servico.Desligar(FormaturaId, UsuarioId, Pedido(motivo: MotivoDeSaida.DificuldadeFinanceira), AutorId, Ct);

        gravado.ShouldNotBeNull();
        gravado.Nome.ShouldBe(NomesDeAuditoria.FormandoDesligado);
        gravado.UsuarioId.ShouldBe(AutorId);
        gravado.Dados.ShouldNotBeNull();
        gravado.Dados.ShouldContain("DificuldadeFinanceira");
        gravado.Dados.ShouldContain("\"parcelasCanceladas\":2");
        gravado.Dados.ShouldContain("\"canceladoEmCentavos\":120000");
    }

    /// <summary>O resumo é o que a comissão vê antes de assinar: o atraso é um recorte do que está em aberto.</summary>
    [Fact]
    public async Task ResumirSaida_soma_pago_em_aberto_e_atraso()
    {
        var hoje = DataUtils.Hoje();
        _parcelas
            .ListarDoVinculo(VinculoId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([
                new ParcelaResumo(
                    Guid.CreateVersion7(),
                    VinculoId,
                    UsuarioId,
                    "João Pedro",
                    TipoDeCobranca.Mensalidade,
                    null,
                    1,
                    3,
                    hoje.AddDays(-60),
                    45_000,
                    StatusDaParcela.Paga,
                    ValorPagoEmCentavos: 45_000
                ),
                new ParcelaResumo(
                    Guid.CreateVersion7(),
                    VinculoId,
                    UsuarioId,
                    "João Pedro",
                    TipoDeCobranca.Mensalidade,
                    null,
                    2,
                    3,
                    hoje.AddDays(-10),
                    45_000,
                    StatusDaParcela.Vencida
                ),
                new ParcelaResumo(
                    Guid.CreateVersion7(),
                    VinculoId,
                    UsuarioId,
                    "João Pedro",
                    TipoDeCobranca.Mensalidade,
                    null,
                    3,
                    3,
                    hoje.AddDays(20),
                    45_000,
                    StatusDaParcela.Aberta
                ),
            ]);

        var resumo = (await Servico.ResumirSaida(FormaturaId, UsuarioId, Ct)).Valor;

        resumo.ShouldNotBeNull();
        resumo.TemAdesao.ShouldBeTrue();
        resumo.JaPagoEmCentavos.ShouldBe(45_000);
        resumo.ParcelasEmAberto.ShouldBe(2);
        resumo.EmAbertoEmCentavos.ShouldBe(90_000);
        resumo.ParcelasEmAtraso.ShouldBe(1);
        resumo.EmAtrasoEmCentavos.ShouldBe(45_000);
    }

    /// <summary>Religar devolve o acesso e <b>não</b> ressuscita parcela: a cobrança volta por lançamento novo.</summary>
    [Fact]
    public async Task Religar_devolve_o_acesso_sem_ressuscitar_parcela_cancelada()
    {
        var vinculo = _vinculo;
        var cancelada = Parcela(30);
        cancelada.Cancelar(DataUtils.Hoje());
        vinculo.Desligar(MotivoDeSaida.Trancamento, null, DateTime.UtcNow.AddDays(-1));
        ComVinculo(vinculo);

        var resultado = await Servico.Religar(FormaturaId, UsuarioId, AutorId, Ct);

        resultado.Sucesso.ShouldBeTrue();
        vinculo.Ativo.ShouldBeTrue();
        vinculo.Desligado.ShouldBeFalse();
        cancelada.Status.ShouldBe(StatusDaParcela.Cancelada);
    }

    /// <summary>Religar quem só foi removido não faz sentido: não há desligamento a desfazer.</summary>
    [Fact]
    public async Task Religar_quem_nao_foi_desligado_e_recusado()
    {
        var vinculo = _vinculo;
        vinculo.Ativo = false;
        ComVinculo(vinculo);

        var resultado = await Servico.Religar(FormaturaId, UsuarioId, AutorId, Ct);

        resultado.Erros.ShouldHaveSingleItem().Codigo.ShouldBe("formatura.membro_nao_desligado");
    }
}
