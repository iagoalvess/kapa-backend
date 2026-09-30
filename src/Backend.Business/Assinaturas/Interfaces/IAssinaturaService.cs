using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Pagamentos.Models;

namespace Backend.Business.Assinaturas.Interfaces;

/// <summary>
/// Catálogo de planos e o lado da comissão na assinatura: contratar, consultar e cancelar.
/// </summary>
/// <remarks>
/// Nada aqui ativa formatura. Quem ativa é o webhook (<see cref="IWebhookService"/>): o retorno do
/// navegador não prova pagamento nenhum.
/// </remarks>
public interface IAssinaturaService
{
    /// <summary>Planos contratáveis.</summary>
    Task<Result<IReadOnlyList<PlanoResumo>>> ListarPlanos(CancellationToken ct = default);

    /// <summary>O plano que vale para a turma agora — o gratuito, se ela não tem plano pago em vigor.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    Task<Result<PlanoDaTurma>> ObterPlanoDaTurma(Guid formaturaId, CancellationToken ct = default);

    /// <summary>A assinatura mais recente da formatura da sessão.</summary>
    Task<Result<AssinaturaDetalhe>> ObterAtual(CancellationToken ct = default);

    /// <summary>
    /// Cria (ou retoma) a assinatura pendente e a sessão de pagamento no provedor.
    /// </summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="dados">Plano escolhido.</param>
    /// <returns>A sessão, com a URL para onde o navegador vai.</returns>
    Task<Result<SessaoDeCheckout>> IniciarCheckout(Guid formaturaId, IniciarCheckout dados, CancellationToken ct = default);

    /// <summary>Cancela a renovação. A vigência paga continua até o fim.</summary>
    Task<Result<AssinaturaDetalhe>> Cancelar(CancellationToken ct = default);

    /// <summary>
    /// Troca o plano da assinatura ativa (P4): a subida cobra a diferença proporcional e vale quando ela for paga; a
    /// descida vale na próxima renovação, se a turma couber.
    /// </summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="planoCodigo">Plano novo, do mesmo ciclo.</param>
    /// <param name="emailDoPagador">Quem paga a diferença.</param>
    /// <returns>A página da diferença, na subida; nula na descida.</returns>
    Task<Result<ResultadoDaTroca>> TrocarPlano(Guid formaturaId, string planoCodigo, string? emailDoPagador, CancellationToken ct = default);

    /// <summary>
    /// Troca o meio da assinatura ativa (P5): a recorrência antiga é cancelada e a nova começa no próximo vencimento,
    /// sem cobrança em dobro.
    /// </summary>
    /// <param name="meio">Meio novo.</param>
    /// <param name="emailDoPagador">Quem autoriza o cartão.</param>
    /// <returns>A página de autorização do cartão; nula na ida para o PIX.</returns>
    Task<Result<ResultadoDaTroca>> TrocarMeio(MeioDePagamento meio, string? emailDoPagador, CancellationToken ct = default);

    /// <summary>A página do PIX da renovação, a partir de 7 dias antes do vencimento. Só no PIX avulso.</summary>
    /// <param name="emailDoPagador">Quem paga.</param>
    Task<Result<SessaoDeCheckout>> PagarCiclo(string? emailDoPagador, CancellationToken ct = default);

    /// <summary>O histórico de pagamentos do plano da formatura da sessão.</summary>
    Task<Result<IReadOnlyList<CobrancaDoPlanoResumo>>> ListarCobrancas(CancellationToken ct = default);
}
