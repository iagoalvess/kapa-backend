using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;

namespace Backend.Business.Assinaturas.Interfaces;

/// <summary>
/// O que o provedor e o relógio dizem sobre a assinatura: webhook e conciliação.
/// </summary>
/// <remarks>
/// Os dois caminhos que mudam status sem ninguém clicar em nada. A conciliação existe porque
/// webhook se perde, e aplica o mesmo efeito pelo mesmo código.
/// </remarks>
public interface IWebhookService
{
    /// <summary>
    /// Recebe um evento do provedor: verifica, registra uma vez e aplica o efeito na mesma transação.
    /// </summary>
    /// <param name="corpo">Corpo cru da requisição.</param>
    /// <param name="assinaturaHmac">Assinatura do corpo, do cabeçalho configurado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ReciboDeWebhook>> Receber(string corpo, string? assinaturaHmac, CancellationToken ct = default);

    /// <summary>
    /// Uma rodada da conciliação: pendentes paradas, vencimentos e avisos.
    /// </summary>
    /// <param name="agoraUtc">Relógio da rodada — parâmetro para o teste controlar o tempo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ResumoDaConciliacao>> Conciliar(DateTime agoraUtc, CancellationToken ct = default);
}
