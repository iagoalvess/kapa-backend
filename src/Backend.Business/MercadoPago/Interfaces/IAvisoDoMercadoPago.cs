using Backend.Business.Abstractions;

namespace Backend.Business.MercadoPago.Interfaces;

/// <summary>
/// O recebedor único dos avisos do Mercado Pago (webhook): confere a assinatura e encaminha o aviso para a conta
/// do Kapa ou para a cobrança da turma (Sprint 25; Sprint 35).
/// </summary>
public interface IAvisoDoMercadoPago
{
    /// <summary>Recebe um aviso.</summary>
    /// <param name="assinatura">Cabeçalho <c>x-signature</c>.</param>
    /// <param name="idDaRequisicao">Cabeçalho <c>x-request-id</c>.</param>
    /// <param name="idDoRecurso">Parâmetro <c>data.id</c> — o id do pedido.</param>
    /// <param name="tipo">Parâmetro <c>type</c>; da turma, só <c>order</c> interessa.</param>
    /// <param name="corpo">O corpo cru, de onde sai o <c>user_id</c>.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> Receber(
        string? assinatura,
        string? idDaRequisicao,
        string? idDoRecurso,
        string? tipo,
        string? corpo,
        CancellationToken ct = default
    );
}
