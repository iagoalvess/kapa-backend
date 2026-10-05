using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// Transforma o plano vigente em parcelas no nome de um formando.
/// </summary>
/// <remarks>
/// Duas portas, e só duas: a adesão (Sprint 7) e o rateio extraordinário (revisão de 17/09/2026).
/// Nenhum outro ponto do sistema cria parcela — um caminho alternativo é como aparece formando
/// devendo sem nunca ter aderido. As duas gravam dentro da transação de quem as chamou.
/// </remarks>
public interface IGeracaoDeParcelasService
{
    /// <summary>
    /// Marca para inclusão as parcelas da cesta e dos rateios que o vínculo ainda não tem.
    /// </summary>
    /// <remarks>
    /// Recebe o plano, e não o busca: é o mesmo que a adesão congelou no snapshot, então o que o
    /// formando leu é o que ele passa a dever. Não chama <c>SalvarAsync</c>: as parcelas entram na
    /// mesma transação da adesão, que salva — como o e-mail enfileirado. Idempotente: gerar de novo
    /// para o mesmo vínculo não cria nada.
    /// </remarks>
    /// <param name="vinculoId">Vínculo de quem adere.</param>
    /// <param name="plano">Plano vigente, com os itens.</param>
    /// <param name="cesta">Os pacotes que o formando escolheu (Sprint 47) — o que não está aqui não vira parcela dele.</param>
    /// <returns>Quantas parcelas foram marcadas.</returns>
    Task<Result<int>> Gerar(Guid vinculoId, PlanoDeCobranca plano, IReadOnlyCollection<ItemDeCobranca> cesta, CancellationToken ct = default);

    /// <summary>
    /// Marca a grade de <b>um</b> item no nome de vários vínculos — o rateio extraordinário.
    /// </summary>
    /// <remarks>
    /// Um item, e não o plano inteiro: passar por <see cref="Gerar"/> completaria de quebra os itens
    /// que a tesouraria escolheu <b>não</b> aplicar a quem já aderiu, e ninguém saberia por quê.
    /// <para>
    /// Como o item acaba de nascer, não há número gerado a pular. A corrida continua com o índice
    /// único <c>(vinculo_id, item_de_cobranca_id, numero)</c>. Não chama <c>SalvarAsync</c>: as
    /// parcelas entram na mesma transação da inclusão do item.
    /// </para>
    /// </remarks>
    /// <param name="vinculoIds">Vínculos alcançados — os ativos que já aderiram.</param>
    /// <param name="item">Item recém-incluído.</param>
    /// <returns>Quantas parcelas foram marcadas.</returns>
    Task<Result<int>> GerarDoItem(IReadOnlyCollection<Guid> vinculoIds, ItemDeCobranca item, CancellationToken ct = default);
}
