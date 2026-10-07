using System.Text.Json;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Settings;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business.Assinaturas.Services;

/// <summary>
/// Webhook e conciliação: os dois caminhos que mudam a assinatura sem ninguém clicar em nada.
/// </summary>
/// <remarks>
/// O log leva só o id e o tipo do evento, nunca o corpo: o corpo vai para
/// <c>eventos_de_cobranca</c> e fica fora de qualquer agregador de log.
/// </remarks>
/// <param name="assinaturaRepository">Assinaturas e eventos de cobrança.</param>
/// <param name="formaturaRepository">A formatura, ativada e suspensa daqui.</param>
/// <param name="vinculoRepository">Presidentes, destinatários dos e-mails.</param>
/// <param name="provedor">PSP que cobra a licença.</param>
/// <param name="emails">E-mails da assinatura.</param>
/// <param name="settings">Carência e janela de conciliação.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class WebhookService(
    IAssinaturaRepository assinaturaRepository,
    IFormaturaRepository formaturaRepository,
    IVinculoRepository vinculoRepository,
    IProvedorDeAssinatura provedor,
    EmailsDeAssinatura emails,
    IOptions<AssinaturaSettings> settings,
    IUnitOfWork unitOfWork,
    ILogger<WebhookService> logger
) : IWebhookService
{
    /// <summary>Linhas por etapa numa rodada de conciliação.</summary>
    /// <remarks>ponytail: lote fixo por rodada horária; o que sobrar fica para a próxima. Paginar quando houver milhares de turmas vencendo na mesma hora.</remarks>
    private const int Lote = 500;

    /// <summary>Checkout pendente há mais que isto é abandono: a conciliação para de consultar o provedor.</summary>
    private const int DiasDeJanelaDaPendente = 7;

    private static readonly HashSet<string> Tratados =
    [
        TiposDeEvento.PagamentoConfirmado,
        TiposDeEvento.PagamentoRecusado,
        TiposDeEvento.AssinaturaRenovada,
        TiposDeEvento.AssinaturaCancelada,
        TiposDeEvento.AssinaturaVencida,
        TiposDeEvento.RecorrenciaAutorizada,
    ];

    private TimeSpan Carencia => TimeSpan.FromDays(settings.Value.DiasDeCarencia);

    /// <inheritdoc />
    /// <remarks>
    /// A assinatura HMAC é conferida antes de qualquer outra coisa, e a recusa não grava nada: o
    /// endpoint é público, e sem isso qualquer um com a URL ativaria a própria formatura.
    /// </remarks>
    public async Task<Result<ReciboDeWebhook>> Receber(string corpo, string? assinaturaHmac, CancellationToken ct = default)
    {
        var leitura = provedor.LerWebhook(corpo, assinaturaHmac);

        if (leitura.Falhou)
        {
            logger.LogWarning("Webhook de assinatura recusado: {Codigo}.", leitura.PrimeiroErro.Codigo);
            return Result.Falha<ReciboDeWebhook>(leitura.Erros);
        }

        return await Processar(leitura.Valor, corpo, DateTime.UtcNow, ct);
    }

    /// <inheritdoc />
    public Task<Result<ReciboDeWebhook>> Aplicar(EventoDoProvedor evento, CancellationToken ct = default) =>
        Processar(evento, JsonSerializer.Serialize(evento), DateTime.UtcNow, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Quatro etapas, cada linha gravada por conta própria — uma turma com problema não segura as outras:
    /// <list type="number">
    /// <item>pendentes paradas: pergunta ao provedor, e o pagamento achado segue o mesmo caminho do webhook;</item>
    /// <item>ativas passadas da vigência: pergunta ao provedor <b>antes</b> de suspender ou de avisar
    /// que venceu — é a renovação cujo webhook se perdeu;</item>
    /// <item>vigência mais carência vencidas: assinatura vencida, formatura suspensa, e-mail ao Presidente;</item>
    /// <item>avisos de D-7, D-3 e D+1.</item>
    /// </list>
    /// </remarks>
    public async Task<Result<ResumoDaConciliacao>> Conciliar(DateTime agoraUtc, CancellationToken ct = default)
    {
        var confirmadas = await ConciliarPendentes(agoraUtc, ct);
        var renovadas = 0;
        var vencidas = 0;
        var avisos = 0;

        var vencendo = await assinaturaRepository.ListarVencendoDeTodasAsFormaturas(agoraUtc.AddDays(Assinatura.MarcosDeAviso.Max()), Lote, ct);

        foreach (var assinatura in vencendo)
        {
            var vencer = assinatura.DeveVencer(agoraUtc, Carencia);
            var marco = vencer ? null : assinatura.AvisoDeVencimentoDevido(agoraUtc);

            if (PassouDaVigencia(vencer, marco) && await AcharRenovacaoPerdida(assinatura, agoraUtc, ct))
            {
                renovadas++;
                continue;
            }

            if (vencer)
            {
                await Vencer(assinatura, ct);
                vencidas++;
            }
            else if (marco is { } devido)
            {
                await Avisar(assinatura, devido, ct);
                avisos++;
            }

            await unitOfWork.SalvarAsync(ct);
        }

        return new ResumoDaConciliacao(confirmadas, renovadas, vencidas, avisos);
    }

    /// <summary>Se a rodada está prestes a agir <b>depois</b> do fim da vigência — suspender, ou avisar que venceu.</summary>
    /// <remarks>
    /// Os marcos positivos (D-7, D-3) são avisos legítimos de quem ainda está em dia: a cobrança da
    /// renovação nem aconteceu, e não há o que reconsultar. Marco negativo é D+1, depois do
    /// vencimento — ali a renovação já deveria ter chegado.
    /// </remarks>
    /// <param name="vencer">Se a assinatura vai ser vencida agora.</param>
    /// <param name="marco">Marco de aviso devido, se houver.</param>
    private static bool PassouDaVigencia(bool vencer, int? marco) => vencer || marco < 0;

    /// <summary>
    /// Pergunta ao provedor se a assinatura foi renovada e o webhook se perdeu. Devolve se achou.
    /// </summary>
    /// <remarks>
    /// É a pendência técnica da Sprint 16: o job só reconsultava as <c>Pendente</c>, então uma
    /// renovação cujo webhook nunca chegou suspendia uma turma que <b>pagou</b>.
    /// <para>
    /// <c>ponytail:</c> consulta na hora de agir, e não a cada rodada da carência. É a única hora em
    /// que a resposta muda o desfecho, e são sete dias de carência × uma rodada por hora — reconsultar
    /// sempre seriam 168 chamadas ao PSP por assinatura para evitar um e-mail a mais.
    /// </para>
    /// <para>
    /// Só a <c>Ativa</c>: quem cancelou a renovação não tem cobrança por vir, e perguntar ao provedor
    /// sobre ela é gastar chamada para ouvir o que já se sabe.
    /// </para>
    /// </remarks>
    /// <param name="assinatura">Assinatura passada da vigência.</param>
    /// <param name="agoraUtc">Momento da rodada.</param>
    private async Task<bool> AcharRenovacaoPerdida(Assinatura assinatura, DateTime agoraUtc, CancellationToken ct) =>
        assinatura is { Status: StatusDaAssinatura.Ativa, Meio: MeioDePagamento.Cartao }
        && await Reconsultar(assinatura.Id, assinatura.IdExterno, agoraUtc, ct)
        && !assinatura.DeveVencer(agoraUtc, Carencia);

    /// <summary>
    /// As assinaturas pendentes no cartão e as cobranças avulsas abertas (o PIX de um ciclo, a diferença de plano)
    /// que o aviso deixou para trás.
    /// </summary>
    /// <remarks>
    /// A pendente no PIX não é consultada pela assinatura: quem é paga é a cobrança aberta dela, que a segunda
    /// lista já cobre — e é por ela que a renovação no PIX também é achada antes de a turma ser suspensa.
    /// </remarks>
    private async Task<int> ConciliarPendentes(DateTime agoraUtc, CancellationToken ct)
    {
        var confirmadas = 0;
        var antesDe = agoraUtc.AddMinutes(-settings.Value.MinutosAntesDeConciliar);
        var depoisDe = agoraUtc.AddDays(-DiasDeJanelaDaPendente);

        var pendentes = await assinaturaRepository.ListarPendentesDeTodasAsFormaturas(antesDe, depoisDe, Lote, ct);

        foreach (var pendente in pendentes.Where(pendente => pendente.Meio == MeioDePagamento.Cartao))
            if (await Reconsultar(pendente.Id, pendente.IdExterno, agoraUtc, ct))
                confirmadas++;

        var abertas = await assinaturaRepository.ListarCobrancasAbertasDeTodasAsFormaturas(antesDe, depoisDe, Lote, ct);

        foreach (var aberta in abertas)
            if (await Reconsultar(aberta.Id, null, agoraUtc, ct))
                confirmadas++;

        return confirmadas;
    }

    /// <summary>Pergunta ao provedor por um pagamento com a referência e o aplica pelo caminho do webhook. Diz se aplicou.</summary>
    /// <param name="referencia">A assinatura, na recorrência; a cobrança, no avulso.</param>
    /// <param name="idExterno">Id da recorrência ou da sessão no provedor.</param>
    /// <param name="agoraUtc">Momento da rodada.</param>
    private async Task<bool> Reconsultar(Guid referencia, string? idExterno, DateTime agoraUtc, CancellationToken ct)
    {
        var consulta = await provedor.ConsultarPagamento(referencia, idExterno, ct);

        if (consulta.Falhou)
        {
            logger.LogWarning("Conciliação não consultou a referência {Referencia}: {Codigo}.", referencia, consulta.PrimeiroErro.Codigo);
            return false;
        }

        if (consulta.Valor is not { } evento)
            return false;

        var recibo = await Processar(evento, JsonSerializer.Serialize(evento), agoraUtc, ct);

        if (recibo.Falhou || recibo.Valor.Duplicado)
            return false;

        logger.LogInformation("Conciliação achou o pagamento da referência {Referencia} sem webhook.", referencia);

        return true;
    }

    /// <summary>
    /// Registra o evento uma vez e aplica o efeito, tudo na mesma transação.
    /// </summary>
    /// <remarks>
    /// Evento repetido, de tipo desconhecido, de assinatura inexistente ou <b>fora de ordem</b>
    /// responde sucesso: erro faria o provedor reentregar para sempre algo que nunca vai dar certo.
    /// <para>
    /// Fora de ordem é evento cuja data no provedor é anterior à do último já aplicado. Ele é
    /// gravado — o corpo fica na tabela, para quem for investigar — mas não aplicado: "fatura
    /// criada" chegando depois de "paga" devolveria a turma para pendente. <b>Menos o pagamento
    /// confirmado</b> (Sprint 37): cada pagamento é dinheiro que entrou, e o PIX de um ciclo pago antes
    /// da diferença de plano, chegando depois dela, não pode ser descartado.
    /// </para>
    /// <para>
    /// O pagamento avulso chega pela cobrança; a assinatura sai dela. Referência que não é cobrança é tentada
    /// como assinatura: o Mercado Pago não garante como marca o pagamento de uma recorrência, e gravar o evento sem
    /// assinatura travaria o id dele — o débito, chegando pelo outro aviso, viraria "repetido".
    /// </para>
    /// </remarks>
    /// <param name="evento">Evento verificado.</param>
    /// <param name="payload">Corpo a guardar.</param>
    /// <param name="agoraUtc">Momento do processamento.</param>
    private Task<Result<ReciboDeWebhook>> Processar(EventoDoProvedor evento, string payload, DateTime agoraUtc, CancellationToken ct) =>
        unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var cobranca = evento.CobrancaId is { } cobrancaId
                    ? await assinaturaRepository.ObterCobrancaParaEdicaoDeTodasAsFormaturas(cobrancaId, token)
                    : null;
                var assinaturaId = cobranca?.AssinaturaId ?? evento.AssinaturaId ?? evento.CobrancaId;
                var assinatura = assinaturaId is { } id ? await assinaturaRepository.ObterParaEdicaoDeTodasAsFormaturas(id, token) : null;
                var foraDeOrdem = evento.Tipo != TiposDeEvento.PagamentoConfirmado && assinatura?.EhAnteriorAoUltimoEvento(evento.OcorridoEm) == true;
                var aplicavel = assinatura is not null && Tratados.Contains(evento.Tipo) && !foraDeOrdem;

                if (foraDeOrdem)
                    logger.LogWarning(
                        "Evento de cobrança {EventoId} ({Tipo}) chegou fora de ordem e foi ignorado: {OcorridoEm} é anterior ao último aplicado.",
                        evento.Id,
                        evento.Tipo,
                        evento.OcorridoEm
                    );

                var novo = await assinaturaRepository.RegistrarSeNovo(
                    new EventoDeCobranca
                    {
                        IdExterno = evento.Id,
                        Tipo = evento.Tipo,
                        AssinaturaId = assinatura?.Id ?? evento.AssinaturaId,
                        FormaturaId = assinatura?.FormaturaId,
                        Payload = payload,
                        RecebidoEm = agoraUtc,
                        ProcessadoEm = aplicavel ? agoraUtc : null,
                    },
                    token
                );

                if (!novo)
                {
                    logger.LogInformation("Evento de cobrança {EventoId} ({Tipo}) repetido; ignorado.", evento.Id, evento.Tipo);
                    return Result.Ok(new ReciboDeWebhook(evento.Id, Duplicado: true));
                }

                if (aplicavel)
                    await Aplicar(evento, assinatura!, cobranca, agoraUtc, token);
                else if (!foraDeOrdem)
                    logger.LogWarning("Evento de cobrança {EventoId} ({Tipo}) gravado e ignorado.", evento.Id, evento.Tipo);

                return Result.Ok(new ReciboDeWebhook(evento.Id, Duplicado: false));
            },
            ct
        );

    /// <summary>O efeito do evento na assinatura, na formatura e no histórico de pagamentos.</summary>
    /// <remarks>
    /// Pagamento confirmado é uma de três coisas: a <b>diferença</b> da subida de plano (o plano novo vale já), a
    /// <b>contratação</b> (a assinatura pendente passa a valer) ou a <b>renovação</b> (o PIX do ciclo ou o débito do
    /// cartão). O pagamento fica no histórico mesmo quando o efeito não se aplica — a turma que pagou o PIX depois de
    /// cancelar pagou, e é o suporte quem estorna.
    /// </remarks>
    /// <param name="evento">Evento novo, já registrado.</param>
    /// <param name="assinatura">Assinatura do evento.</param>
    /// <param name="cobranca">A cobrança avulsa paga, quando o evento é de uma.</param>
    /// <param name="agoraUtc">Momento do processamento.</param>
    private async Task Aplicar(
        EventoDoProvedor evento,
        Assinatura assinatura,
        CobrancaDaAssinatura? cobranca,
        DateTime agoraUtc,
        CancellationToken ct
    )
    {
        var formatura =
            await formaturaRepository.ObterParaEdicao(assinatura.FormaturaId, ct)
            ?? throw new InvalidOperationException($"Assinatura {assinatura.Id} sem formatura.");

        var ciclo = (await assinaturaRepository.ObterPlano(cobranca?.PlanoId ?? assinatura.PlanoId, ct))?.Ciclo ?? CicloDeCobranca.Mensal;
        var pago = evento.Tipo == TiposDeEvento.PagamentoConfirmado;
        var diferenca = pago && cobranca?.Motivo == MotivoDaCobranca.Diferenca;
        var contratacao = pago && !diferenca && assinatura.Status == StatusDaAssinatura.Pendente;

        var efeito = evento.Tipo switch
        {
            TiposDeEvento.PagamentoConfirmado when diferenca => assinatura.SubirDePlano(cobranca!.PlanoId),
            TiposDeEvento.PagamentoConfirmado when contratacao => Contratar(assinatura, evento, cobranca, agoraUtc, ciclo),
            TiposDeEvento.PagamentoConfirmado or TiposDeEvento.AssinaturaRenovada => assinatura.Renovar(agoraUtc, ciclo),
            TiposDeEvento.AssinaturaCancelada when DaRecorrenciaAtual(evento, assinatura) => assinatura.Cancelar(agoraUtc),
            TiposDeEvento.RecorrenciaAutorizada when DaRecorrenciaAtual(evento, assinatura) => AutorizarCartao(assinatura),
            TiposDeEvento.AssinaturaCancelada or TiposDeEvento.RecorrenciaAutorizada => RecorrenciaAntiga,
            TiposDeEvento.AssinaturaVencida => assinatura.Vencer(),
            _ => Result.Ok(),
        };

        if (pago)
            await RegistrarPagamento(evento, assinatura, cobranca, agoraUtc, ct);

        if (efeito.Falhou)
        {
            logger.LogWarning("Evento de cobrança {EventoId} ({Tipo}) não aplicado: {Codigo}.", evento.Id, evento.Tipo, efeito.PrimeiroErro.Codigo);
            return;
        }

        assinatura.RegistrarEvento(evento.OcorridoEm);

        if (diferenca)
            await AjustarRecorrencia(assinatura, ct);
        else if (contratacao)
        {
            if (assinatura.CupomId is not null)
                await AjustarRecorrencia(assinatura, ct);

            Ativar(formatura);
            await emails.BoasVindas(formatura, await Presidentes(formatura, ct), assinatura.VigenteAte!.Value, ct);
        }
        else if (pago || evento.Tipo == TiposDeEvento.AssinaturaRenovada)
            Ativar(formatura);
        else if (evento.Tipo == TiposDeEvento.AssinaturaVencida)
            await Suspender(formatura, ct);
        else if (evento.Tipo == TiposDeEvento.PagamentoRecusado)
            await emails.PagamentoRecusado(formatura, await Presidentes(formatura, ct), ct);
    }

    /// <summary>O aviso é de uma recorrência que a assinatura já trocou — a turma saiu do cartão, ou autorizou outro.</summary>
    private static readonly Result RecorrenciaAntiga = Result.Falha(
        Erro.Conflito("assinatura.recorrencia_antiga", "O aviso é de uma recorrência que a assinatura não usa mais.")
    );

    /// <summary>
    /// Se o aviso é da recorrência que a assinatura usa agora. O Kapa cancela a recorrência antiga quando a turma troca
    /// de meio (P5), e o "cancelada" que o Mercado Pago manda em seguida não pode cancelar a assinatura.
    /// </summary>
    private static bool DaRecorrenciaAtual(EventoDoProvedor evento, Assinatura assinatura) =>
        evento.IdExternoDaAssinatura is null || evento.IdExternoDaAssinatura == assinatura.IdExterno;

    /// <summary>A troca para o cartão foi autorizada: os próximos ciclos saem da recorrência.</summary>
    private static Result AutorizarCartao(Assinatura assinatura)
    {
        assinatura.Meio = MeioDePagamento.Cartao;

        return Result.Ok();
    }

    /// <summary>
    /// A contratação paga: pelo PIX, vale o plano da cobrança — a turma pode ter trocado de plano antes de pagar; no
    /// cartão, a recorrência que o provedor informa passa a ser a da assinatura.
    /// </summary>
    private static Result Contratar(
        Assinatura assinatura,
        EventoDoProvedor evento,
        CobrancaDaAssinatura? cobranca,
        DateTime agoraUtc,
        CicloDeCobranca ciclo
    )
    {
        if (cobranca is not null)
            assinatura.PlanoId = cobranca.PlanoId;
        else if (evento.IdExternoDaAssinatura is { } recorrencia)
            assinatura.IdExterno = recorrencia;

        return assinatura.ConfirmarPagamento(agoraUtc, ciclo);
    }

    /// <summary>
    /// Registra o pagamento no histórico: a cobrança avulsa vira paga; o débito do cartão, que o Kapa não abriu, nasce
    /// paga, no plano que a assinatura tem depois do efeito — o da descida agendada, quando a renovação a aplicou.
    /// </summary>
    private async Task RegistrarPagamento(
        EventoDoProvedor evento,
        Assinatura assinatura,
        CobrancaDaAssinatura? cobranca,
        DateTime agoraUtc,
        CancellationToken ct
    )
    {
        if (cobranca is null)
        {
            var plano = await assinaturaRepository.ObterPlano(assinatura.PlanoId, ct);

            cobranca = CobrancaDaAssinatura.Abrir(
                assinatura.Id,
                assinatura.PlanoId,
                MotivoDaCobranca.Ciclo,
                MeioDePagamento.Cartao,
                evento.ValorEmCentavos ?? plano?.PrecoEmCentavos ?? 0
            );
            await assinaturaRepository.AdicionarCobranca(cobranca, ct);
        }

        var registro = cobranca.Pagar(evento.IdDoPagamento ?? evento.Id, evento.ValorEmCentavos, agoraUtc);

        if (registro.Falhou)
            logger.LogWarning(
                "Pagamento {EventoId} da cobrança {CobrancaId} não registrado: {Codigo}.",
                evento.Id,
                cobranca.Id,
                registro.PrimeiroErro.Codigo
            );
    }

    /// <summary>
    /// Depois da diferença paga, o débito do cartão passa a cobrar o plano novo — e, depois da contratação com cupom
    /// (Sprint 51), o preço cheio: o desconto vale só na primeira cobrança. Falhar aqui não desfaz nada: o log
    /// avisa como erro (é o alerta do Grafana), e o próximo débito sai pelo valor antigo até o suporte acertar.
    /// </summary>
    private async Task AjustarRecorrencia(Assinatura assinatura, CancellationToken ct)
    {
        if (assinatura is not { Meio: MeioDePagamento.Cartao, IdExterno: { } recorrencia })
            return;

        var plano = await assinaturaRepository.ObterPlano(assinatura.PlanoId, ct);

        if (plano is null)
            return;

        var ajuste = await provedor.AtualizarValor(recorrencia, plano.PrecoEmCentavos, ct);

        if (ajuste.Falhou)
            logger.LogError(
                "A recorrência da assinatura {AssinaturaId} não passou para o valor do plano {Plano}: {Codigo}.",
                assinatura.Id,
                plano.Codigo,
                ajuste.PrimeiroErro.Codigo
            );
    }

    /// <summary>Ativa a formatura, se ainda não estiver. Suspensa passa; encerrada e descartada ficam.</summary>
    /// <remarks>A sequência mora em <see cref="Formatura.AtivarPorPagamento"/>, porque o painel de suporte usa a mesma.</remarks>
    /// <param name="formatura">Formatura da assinatura paga.</param>
    private void Ativar(Formatura formatura)
    {
        var transicao = formatura.AtivarPorPagamento();

        if (transicao.Falhou)
            logger.LogWarning(
                "Pagamento da formatura {FormaturaId} confirmado, mas ela não pôde ser ativada: {Codigo}.",
                formatura.Id,
                transicao.PrimeiroErro.Codigo
            );
    }

    private async Task Vencer(Assinatura assinatura, CancellationToken ct)
    {
        assinatura.Vencer();

        var formatura = await formaturaRepository.ObterParaEdicao(assinatura.FormaturaId, ct);

        if (formatura is not null)
            await Suspender(formatura, ct);
    }

    /// <summary>Suspende a formatura ativa e avisa o Presidente. Nada é apagado: a turma segue lendo.</summary>
    /// <param name="formatura">Formatura da assinatura vencida.</param>
    private async Task Suspender(Formatura formatura, CancellationToken ct)
    {
        if (formatura.Transicionar(StatusDaFormatura.Suspensa).Falhou)
            return;

        await emails.Suspensao(formatura, await Presidentes(formatura, ct), ct);
    }

    /// <summary>Avisa o vencimento — menos o "renova em tal dia, nada muda" de quem renova sozinho no cartão.</summary>
    /// <remarks>
    /// Antes do vencimento, a assinatura no cartão que segue ativa não pede nada a ninguém: eram dois e-mails por ciclo
    /// só para dizer que tudo continua (07/10/2026). O marco fica registrado do mesmo jeito; o PIX avulso, a renovação
    /// cancelada e o D+1 continuam saindo, porque pedem uma ação.
    /// </remarks>
    private async Task Avisar(Assinatura assinatura, int marco, CancellationToken ct)
    {
        assinatura.RegistrarAviso(marco);

        if (marco > 0 && assinatura.Status != StatusDaAssinatura.Cancelada && assinatura.Meio != MeioDePagamento.Pix)
            return;

        var formatura = await formaturaRepository.ObterParaEdicao(assinatura.FormaturaId, ct);

        if (formatura is null)
            return;

        var vigenteAte = assinatura.VigenteAte!.Value;
        var cancelada = assinatura.Status == StatusDaAssinatura.Cancelada;

        await emails.AvisoDeVencimento(
            formatura,
            await Presidentes(formatura, ct),
            marco,
            vigenteAte,
            cancelada ? vigenteAte : vigenteAte + Carencia,
            cancelada,
            ct
        );
    }

    private Task<IReadOnlyList<string>> Presidentes(Formatura formatura, CancellationToken ct) =>
        vinculoRepository.ListarEmailsDosPresidentes(formatura.Id, ct);
}
