using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;

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
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<IReadOnlyList<PlanoResumo>>> ListarPlanos(CancellationToken ct = default);

    /// <summary>A assinatura mais recente da formatura da sessão.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<AssinaturaDetalhe>> ObterAtual(CancellationToken ct = default);

    /// <summary>
    /// Cria (ou retoma) a assinatura pendente e a sessão de pagamento no provedor.
    /// </summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="dados">Plano escolhido.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>A sessão, com a URL para onde o navegador vai.</returns>
    Task<Result<SessaoDeCheckout>> IniciarCheckout(Guid formaturaId, IniciarCheckout dados, CancellationToken ct = default);

    /// <summary>Cancela a renovação. A vigência paga continua até o fim.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<AssinaturaDetalhe>> Cancelar(CancellationToken ct = default);
}
