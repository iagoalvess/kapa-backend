using Backend.Business.Abstractions;

namespace Backend.Business.Assinaturas.Interfaces;

/// <summary>
/// Os avisos do Mercado Pago sobre a conta do próprio Kapa — os planos das turmas (Sprint 37). O recebedor
/// único (<c>AvisoDoMercadoPago</c>) já conferiu a assinatura e separou estes dos da conta de uma turma.
/// </summary>
/// <remarks>
/// <c>ponytail:</c> uma implementação por enquanto, a que só registra; a Sprint 37 troca pela que consulta a
/// recorrência ou o pedido com o token do Kapa e aplica o evento na assinatura.
/// </remarks>
public interface IAvisosDaContaDoKapa
{
    /// <summary>Recebe um aviso já autenticado. Falha indisponível faz o Mercado Pago reentregar.</summary>
    /// <param name="tipo">O tópico (<c>type</c>): <c>order</c>, <c>subscription_preapproval</c>…</param>
    /// <param name="idDoRecurso">O <c>data.id</c> do aviso.</param>
    Task<Result> Receber(string? tipo, string? idDoRecurso, CancellationToken ct = default);
}
