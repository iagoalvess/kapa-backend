using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// Transforma o plano vigente em parcelas no nome de um formando.
/// </summary>
/// <remarks>
/// Chamado <b>só</b> pela adesão (Sprint 7), dentro da transação dela. Nenhum outro ponto do
/// sistema cria parcela: um caminho alternativo é como aparece formando devendo sem nunca ter
/// aderido.
/// </remarks>
public interface IGeracaoDeParcelasService
{
    /// <summary>
    /// Marca para inclusão as parcelas do plano que o vínculo ainda não tem.
    /// </summary>
    /// <remarks>
    /// Recebe o plano, e não o busca: é o mesmo que a adesão congelou no snapshot, então o que o
    /// formando leu é o que ele passa a dever. Não chama <c>SalvarAsync</c>: as parcelas entram na
    /// mesma transação da adesão, que salva — como o e-mail enfileirado. Idempotente: gerar de novo
    /// para o mesmo vínculo não cria nada.
    /// </remarks>
    /// <param name="vinculoId">Vínculo de quem adere.</param>
    /// <param name="plano">Plano vigente, com os itens.</param>
    /// <returns>Quantas parcelas foram marcadas.</returns>
    Task<Result<int>> Gerar(Guid vinculoId, PlanoDeCobranca plano, CancellationToken ct = default);
}
