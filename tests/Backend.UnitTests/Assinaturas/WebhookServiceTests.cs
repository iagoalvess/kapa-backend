using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Services;
using Backend.Business.Assinaturas.Settings;
using Backend.Business.Common;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Backend.UnitTests.Assinaturas;

/// <summary>
/// Webhook e conciliação: verificação antes de tudo, uma aplicação por evento, e o mesmo efeito pelos dois caminhos.
/// </summary>
public sealed class WebhookServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IAssinaturaRepository _assinaturas = Substitute.For<IAssinaturaRepository>();
    private readonly IFormaturaRepository _formaturas = Substitute.For<IFormaturaRepository>();
    private readonly IVinculoRepository _vinculos = Substitute.For<IVinculoRepository>();
    private readonly IProvedorDeAssinatura _provedor = Substitute.For<IProvedorDeAssinatura>();
    private readonly IEmailService _email = Substitute.For<IEmailService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly Formatura _formatura = new() { Nome = "Medicina 2027" };
    private readonly Assinatura _assinatura = new();

    public WebhookServiceTests()
    {
        // O padrão é "o provedor não tem nada a informar". Sem isto, toda conciliação que chega ao
        // ponto de reconsultar a renovação (Sprint 16) receberia um `Result` nulo do substitute.
        _provedor.ConsultarPagamento(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Result.Ok<EventoDoProvedor?>(null));

        _unitOfWork
            .EmTransacaoAsync(Arg.Any<Func<CancellationToken, Task<Result<ReciboDeWebhook>>>>(), Arg.Any<CancellationToken>())
            .Returns(chamada => chamada.Arg<Func<CancellationToken, Task<Result<ReciboDeWebhook>>>>()(CancellationToken.None));

        _assinaturas.ObterParaEdicaoDeTodasAsFormaturas(_assinatura.Id, Arg.Any<CancellationToken>()).Returns(_assinatura);
        _assinaturas.ObterPlano(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new Plano { Ciclo = CicloDeCobranca.Mensal });
        _assinaturas.RegistrarSeNovo(Arg.Any<EventoDeCobranca>(), Arg.Any<CancellationToken>()).Returns(true);
        _formaturas.ObterParaEdicao(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_formatura);
        _vinculos.ListarEmailsDosPresidentes(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(["presidente@turma.com"]);
    }

    private WebhookService Servico =>
        new(
            _assinaturas,
            _formaturas,
            _vinculos,
            _provedor,
            new EmailsDeAssinatura(_email, Options.Create(new AplicacaoSettings())),
            Options.Create(new AssinaturaSettings()),
            _unitOfWork,
            NullLogger<WebhookService>.Instance
        );

    private EventoDoProvedor Evento(string tipo) => new("evt_1", tipo, _assinatura.Id, "sub_1");

    private void ProvedorEntrega(EventoDoProvedor evento) => _provedor.LerWebhook("corpo", "hmac").Returns(Result.Ok(evento));

    [Fact]
    public async Task Hmac_invalido_nao_grava_nada()
    {
        // Arrange
        _provedor
            .LerWebhook(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(Result.Falha<EventoDoProvedor>(Erro.NaoAutenticado("webhook.assinatura_invalida", "Inválida.")));

        // Act
        var resultado = await Servico.Receber("corpo", "forjado", Ct);

        // Assert
        resultado.Erros.ShouldHaveSingleItem().Tipo.ShouldBe(ETipoErro.NaoAutenticado);
        await _assinaturas.DidNotReceiveWithAnyArgs().RegistrarSeNovo(default!, Ct);
        await _unitOfWork.DidNotReceiveWithAnyArgs().EmTransacaoAsync<Result<ReciboDeWebhook>>(default!, Ct);
    }

    [Fact]
    public async Task Pagamento_confirmado_ativa_assinatura_e_formatura_e_avisa_o_presidente()
    {
        ProvedorEntrega(Evento(TiposDeEvento.PagamentoConfirmado));

        var resultado = await Servico.Receber("corpo", "hmac", Ct);

        resultado.Valor.Duplicado.ShouldBeFalse();
        _assinatura.Status.ShouldBe(StatusDaAssinatura.Ativa);
        _assinatura.IdExterno.ShouldBe("sub_1");
        _formatura.Status.ShouldBe(StatusDaFormatura.Ativa);
        await _email.Received(1).Enfileirar(Arg.Is<NovoEmail>(e => e.Para == "presidente@turma.com"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Evento_repetido_responde_sucesso_sem_reaplicar()
    {
        ProvedorEntrega(Evento(TiposDeEvento.PagamentoConfirmado));
        _assinaturas.RegistrarSeNovo(Arg.Any<EventoDeCobranca>(), Arg.Any<CancellationToken>()).Returns(false);

        var resultado = await Servico.Receber("corpo", "hmac", Ct);

        resultado.Valor.Duplicado.ShouldBeTrue();
        _assinatura.Status.ShouldBe(StatusDaAssinatura.Pendente);
        _formatura.Status.ShouldBe(StatusDaFormatura.Ativa);
    }

    /// <summary>Erro para evento que não interessa faz o provedor reentregar para sempre.</summary>
    [Fact]
    public async Task Tipo_desconhecido_e_gravado_sem_processamento_e_responde_sucesso()
    {
        ProvedorEntrega(Evento("fatura.criada"));

        var resultado = await Servico.Receber("corpo", "hmac", Ct);

        resultado.Sucesso.ShouldBeTrue();
        await _assinaturas
            .Received(1)
            .RegistrarSeNovo(Arg.Is<EventoDeCobranca>(e => e.Tipo == "fatura.criada" && e.ProcessadoEm == null), Arg.Any<CancellationToken>());
        _assinatura.Status.ShouldBe(StatusDaAssinatura.Pendente);
    }

    /// <summary>A turma que pagou, o webhook que não chegou: a conciliação acha no provedor.</summary>
    [Fact]
    public async Task Conciliacao_confirma_pendente_paga_sem_webhook()
    {
        _assinaturas
            .ListarPendentesDeTodasAsFormaturas(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([_assinatura]);
        _provedor
            .ConsultarPagamento(_assinatura.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok<EventoDoProvedor?>(Evento(TiposDeEvento.PagamentoConfirmado)));

        var resumo = (await Servico.Conciliar(DateTime.UtcNow, Ct)).Valor;

        resumo.Confirmadas.ShouldBe(1);
        _assinatura.Status.ShouldBe(StatusDaAssinatura.Ativa);
        _formatura.Status.ShouldBe(StatusDaFormatura.Ativa);
    }

    [Fact]
    public async Task Conciliacao_vence_depois_da_carencia_e_suspende_a_formatura()
    {
        var pagaEm = DateTime.UtcNow.AddDays(-40);
        _assinatura.ConfirmarPagamento(pagaEm, CicloDeCobranca.Mensal);
        _formatura.Transicionar(StatusDaFormatura.Ativa);
        _assinaturas.ListarVencendoDeTodasAsFormaturas(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([_assinatura]);

        var resumo = (await Servico.Conciliar(DateTime.UtcNow, Ct)).Valor;

        resumo.Vencidas.ShouldBe(1);
        _assinatura.Status.ShouldBe(StatusDaAssinatura.Vencida);
        _formatura.Status.ShouldBe(StatusDaFormatura.Suspensa);
        await _email.Received(1).Enfileirar(Arg.Any<NovoEmail>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received().SalvarAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Dentro da carência não suspende — manda o aviso de D+1.</summary>
    [Fact]
    public async Task Conciliacao_dentro_da_carencia_so_avisa()
    {
        var pagaEm = DateTime.UtcNow.AddMonths(-1).AddDays(-2);
        _assinatura.ConfirmarPagamento(pagaEm, CicloDeCobranca.Mensal);
        _formatura.Transicionar(StatusDaFormatura.Ativa);
        _assinaturas.ListarVencendoDeTodasAsFormaturas(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([_assinatura]);

        var resumo = (await Servico.Conciliar(DateTime.UtcNow, Ct)).Valor;

        resumo.ShouldBe(new ResumoDaConciliacao(0, 0, 0, 1));
        _assinatura.UltimoAvisoDeVencimento.ShouldBe(-1);
        _formatura.Status.ShouldBe(StatusDaFormatura.Ativa);
    }

    /// <summary>
    /// Sprint 16: evento antigo que chega depois de um novo não pode desfazer o estado.
    /// </summary>
    /// <remarks>
    /// O caso real é a reentrega do PSP: "pagamento recusado" de terça chegando depois do
    /// "renovada" de quarta. Sem a comparação por data, a turma em dia era suspensa.
    /// </remarks>
    [Fact]
    public async Task Evento_fora_de_ordem_e_gravado_mas_nao_aplicado()
    {
        // Arrange — a confirmação de hoje já foi aplicada.
        var hoje = new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
        ProvedorEntrega(Evento(TiposDeEvento.PagamentoConfirmado) with { OcorridoEm = hoje });
        await Servico.Receber("corpo", "hmac", Ct);
        _assinatura.Status.ShouldBe(StatusDaAssinatura.Ativa);

        // Act — e agora chega a recusa de ontem, atrasada.
        ProvedorEntrega(new EventoDoProvedor("evt_0", TiposDeEvento.PagamentoRecusado, _assinatura.Id, "sub_1", hoje.AddDays(-1)));
        var resultado = await Servico.Receber("corpo", "hmac", Ct);

        // Assert — responde sucesso (senão o PSP reentrega para sempre), grava e não aplica.
        resultado.Sucesso.ShouldBeTrue();
        _assinatura.Status.ShouldBe(StatusDaAssinatura.Ativa);
        _assinatura.UltimoEventoEm.ShouldBe(hoje);
        await _assinaturas
            .Received(1)
            .RegistrarSeNovo(Arg.Is<EventoDeCobranca>(e => e.IdExterno == "evt_0" && e.ProcessadoEm == null), Arg.Any<CancellationToken>());
    }

    /// <summary>Evento sem data do provedor continua sendo aplicado: não há como ordenar.</summary>
    [Fact]
    public async Task Evento_sem_data_do_provedor_continua_sendo_aplicado()
    {
        ProvedorEntrega(Evento(TiposDeEvento.PagamentoConfirmado));

        await Servico.Receber("corpo", "hmac", Ct);

        _assinatura.Status.ShouldBe(StatusDaAssinatura.Ativa);
        _assinatura.UltimoEventoEm.ShouldBeNull();
    }

    /// <summary>
    /// Sprint 16: renovação cobrada cujo webhook se perdeu não pode suspender quem pagou.
    /// </summary>
    /// <remarks>
    /// Antes, o job só reconsultava as <c>Pendente</c>. Agora, pouco antes de suspender, ele
    /// pergunta ao provedor — e a resposta chega pelo mesmo caminho do webhook.
    /// </remarks>
    [Fact]
    public async Task Conciliacao_acha_renovacao_perdida_antes_de_suspender()
    {
        // Arrange — vigência acabada há mais que a carência: sem a reconsulta, seria suspensa.
        var agora = DateTime.UtcNow;
        _assinatura.ConfirmarPagamento(agora.AddDays(-40), CicloDeCobranca.Mensal);
        _formatura.Transicionar(StatusDaFormatura.Ativa);
        _assinaturas.ListarVencendoDeTodasAsFormaturas(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([_assinatura]);
        _provedor
            .ConsultarPagamento(_assinatura.Id, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok<EventoDoProvedor?>(Evento(TiposDeEvento.AssinaturaRenovada) with { OcorridoEm = agora }));

        // Act
        var resumo = (await Servico.Conciliar(agora, Ct)).Valor;

        // Assert
        resumo.Renovadas.ShouldBe(1);
        resumo.Vencidas.ShouldBe(0);
        _assinatura.Status.ShouldBe(StatusDaAssinatura.Ativa);
        _assinatura.VigenteAte!.Value.ShouldBeGreaterThan(agora);
        _formatura.Status.ShouldBe(StatusDaFormatura.Ativa);
    }

    /// <summary>Provedor sem renovação a informar: a suspensão acontece, como antes.</summary>
    [Fact]
    public async Task Conciliacao_suspende_quando_o_provedor_nao_tem_renovacao()
    {
        var agora = DateTime.UtcNow;
        _assinatura.ConfirmarPagamento(agora.AddDays(-40), CicloDeCobranca.Mensal);
        _formatura.Transicionar(StatusDaFormatura.Ativa);
        _assinaturas.ListarVencendoDeTodasAsFormaturas(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([_assinatura]);

        var resumo = (await Servico.Conciliar(agora, Ct)).Valor;

        resumo.Renovadas.ShouldBe(0);
        resumo.Vencidas.ShouldBe(1);
        _formatura.Status.ShouldBe(StatusDaFormatura.Suspensa);
    }
}
