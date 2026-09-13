using System.Text.Json;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Assinaturas.Settings;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
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
    /// <remarks>
    /// Três etapas, cada linha gravada por conta própria — uma turma com problema não segura as outras:
    /// <list type="number">
    /// <item>pendentes paradas: pergunta ao provedor, e o pagamento achado segue o mesmo caminho do webhook;</item>
    /// <item>vigência mais carência vencidas: assinatura vencida, formatura suspensa, e-mail ao Presidente;</item>
    /// <item>avisos de D-7, D-3 e D+1.</item>
    /// </list>
    /// </remarks>
    public async Task<Result<ResumoDaConciliacao>> Conciliar(DateTime agoraUtc, CancellationToken ct = default)
    {
        var confirmadas = await ConciliarPendentes(agoraUtc, ct);
        var vencidas = 0;
        var avisos = 0;

        var vencendo = await assinaturaRepository.ListarVencendoDeTodasAsFormaturas(agoraUtc.AddDays(Assinatura.MarcosDeAviso.Max()), Lote, ct);

        foreach (var assinatura in vencendo)
        {
            if (assinatura.DeveVencer(agoraUtc, Carencia))
            {
                await Vencer(assinatura, ct);
                vencidas++;
            }
            else if (assinatura.AvisoDeVencimentoDevido(agoraUtc) is { } marco)
            {
                await Avisar(assinatura, marco, ct);
                avisos++;
            }

            await unitOfWork.SalvarAsync(ct);
        }

        return new ResumoDaConciliacao(confirmadas, vencidas, avisos);
    }

    private async Task<int> ConciliarPendentes(DateTime agoraUtc, CancellationToken ct)
    {
        var confirmadas = 0;

        var pendentes = await assinaturaRepository.ListarPendentesDeTodasAsFormaturas(
            agoraUtc.AddMinutes(-settings.Value.MinutosAntesDeConciliar),
            agoraUtc.AddDays(-DiasDeJanelaDaPendente),
            Lote,
            ct
        );

        foreach (var pendente in pendentes)
        {
            var consulta = await provedor.ConsultarPagamento(pendente.Id, pendente.IdExterno, ct);

            if (consulta.Falhou)
            {
                logger.LogWarning("Conciliação não consultou a assinatura {AssinaturaId}: {Codigo}.", pendente.Id, consulta.PrimeiroErro.Codigo);
                continue;
            }

            if (consulta.Valor is not { } evento)
                continue;

            var recibo = await Processar(evento, JsonSerializer.Serialize(evento), agoraUtc, ct);

            if (recibo.Sucesso && !recibo.Valor.Duplicado)
            {
                logger.LogInformation("Conciliação confirmou a assinatura {AssinaturaId} sem webhook.", pendente.Id);
                confirmadas++;
            }
        }

        return confirmadas;
    }

    /// <summary>
    /// Registra o evento uma vez e aplica o efeito, tudo na mesma transação.
    /// </summary>
    /// <remarks>
    /// Evento repetido, de tipo desconhecido ou de assinatura inexistente responde sucesso: erro faria
    /// o provedor reentregar para sempre algo que nunca vai dar certo.
    /// </remarks>
    /// <param name="evento">Evento verificado.</param>
    /// <param name="payload">Corpo a guardar.</param>
    /// <param name="agoraUtc">Momento do processamento.</param>
    /// <param name="ct">Token de cancelamento.</param>
    private Task<Result<ReciboDeWebhook>> Processar(EventoDoProvedor evento, string payload, DateTime agoraUtc, CancellationToken ct) =>
        unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var assinatura = evento.AssinaturaId is { } id ? await assinaturaRepository.ObterParaEdicaoDeTodasAsFormaturas(id, token) : null;
                var aplicavel = assinatura is not null && Tratados.Contains(evento.Tipo);

                var novo = await assinaturaRepository.RegistrarSeNovo(
                    new EventoDeCobranca
                    {
                        IdExterno = evento.Id,
                        Tipo = evento.Tipo,
                        AssinaturaId = evento.AssinaturaId,
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
                    await Aplicar(evento, assinatura!, agoraUtc, token);
                else
                    logger.LogWarning("Evento de cobrança {EventoId} ({Tipo}) gravado e ignorado.", evento.Id, evento.Tipo);

                return Result.Ok(new ReciboDeWebhook(evento.Id, Duplicado: false));
            },
            ct
        );

    private async Task Aplicar(EventoDoProvedor evento, Assinatura assinatura, DateTime agoraUtc, CancellationToken ct)
    {
        var formatura =
            await formaturaRepository.ObterParaEdicao(assinatura.FormaturaId, ct)
            ?? throw new InvalidOperationException($"Assinatura {assinatura.Id} sem formatura.");

        var ciclo = (await assinaturaRepository.ObterPlano(assinatura.PlanoId, ct))?.Ciclo ?? CicloDeCobranca.Mensal;

        if (evento.IdExternoDaAssinatura is { } idExterno)
            assinatura.IdExterno = idExterno;

        var efeito = evento.Tipo switch
        {
            TiposDeEvento.PagamentoConfirmado => assinatura.ConfirmarPagamento(agoraUtc, ciclo),
            TiposDeEvento.AssinaturaRenovada => assinatura.Renovar(agoraUtc, ciclo),
            TiposDeEvento.AssinaturaCancelada => assinatura.Cancelar(agoraUtc),
            TiposDeEvento.AssinaturaVencida => assinatura.Vencer(),
            _ => Result.Ok(),
        };

        if (efeito.Falhou)
        {
            logger.LogWarning("Evento de cobrança {EventoId} ({Tipo}) não aplicado: {Codigo}.", evento.Id, evento.Tipo, efeito.PrimeiroErro.Codigo);
            return;
        }

        switch (evento.Tipo)
        {
            case TiposDeEvento.PagamentoConfirmado:
                Ativar(formatura);
                await emails.BoasVindas(formatura, await Presidentes(formatura, ct), assinatura.VigenteAte!.Value, ct);
                break;

            case TiposDeEvento.AssinaturaRenovada:
                Ativar(formatura);
                break;

            case TiposDeEvento.AssinaturaVencida:
                await Suspender(formatura, ct);
                break;

            case TiposDeEvento.PagamentoRecusado:
                await emails.PagamentoRecusado(formatura, await Presidentes(formatura, ct), ct);
                break;
        }
    }

    /// <summary>Ativa a formatura, se ainda não estiver. Rascunho, aguardando e suspensa passam; encerrada fica.</summary>
    /// <param name="formatura">Formatura da assinatura paga.</param>
    private void Ativar(Formatura formatura)
    {
        if (formatura.Status == StatusDaFormatura.Ativa)
            return;

        if (formatura.Status == StatusDaFormatura.Rascunho)
            formatura.Transicionar(StatusDaFormatura.AguardandoPagamento);

        var transicao = formatura.Transicionar(StatusDaFormatura.Ativa);

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
    /// <param name="ct">Token de cancelamento.</param>
    private async Task Suspender(Formatura formatura, CancellationToken ct)
    {
        if (formatura.Transicionar(StatusDaFormatura.Suspensa).Falhou)
            return;

        await emails.Suspensao(formatura, await Presidentes(formatura, ct), ct);
    }

    private async Task Avisar(Assinatura assinatura, int marco, CancellationToken ct)
    {
        assinatura.RegistrarAviso(marco);

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
