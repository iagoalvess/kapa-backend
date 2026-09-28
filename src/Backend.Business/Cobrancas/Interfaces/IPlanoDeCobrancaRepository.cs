using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// Planos de cobrança da formatura selecionada.
/// </summary>
/// <remarks>Isolados pelo filtro global: nenhum método recebe a formatura.</remarks>
public interface IPlanoDeCobrancaRepository
{
    /// <summary>Os planos da turma, o vigente primeiro e depois os mais novos.</summary>
    Task<IReadOnlyList<PlanoDeCobrancaResumo>> Listar(CancellationToken ct = default);

    /// <summary>O plano com todos os itens, sem rastreamento; nulo se não existir nesta turma.</summary>
    /// <param name="planoId">Plano.</param>
    Task<PlanoDeCobranca?> Obter(Guid planoId, CancellationToken ct = default);

    /// <summary>O plano com todos os itens, rastreado para alteração; nulo se não existir nesta turma.</summary>
    /// <param name="planoId">Plano.</param>
    Task<PlanoDeCobranca?> ObterParaEdicao(Guid planoId, CancellationToken ct = default);

    /// <summary>O plano em vigor, com os itens, sem rastreamento; nulo se a turma não tiver.</summary>
    Task<PlanoDeCobranca?> ObterVigente(CancellationToken ct = default);

    /// <summary>O plano em vigor, com os itens, rastreado para alteração; nulo se a turma não tiver.</summary>
    /// <remarks>
    /// Os opcionais (Sprint 20) moram no plano vigente e são editados sem que a tela mande o id dele — é o
    /// mesmo "um plano vigente por turma" que a adesão já usa, agora do lado da escrita.
    /// </remarks>
    Task<PlanoDeCobranca?> ObterVigenteParaEdicao(CancellationToken ct = default);

    /// <summary>Se a turma já tem um plano em vigor.</summary>
    Task<bool> ExisteVigente(CancellationToken ct = default);

    /// <summary>Marca um plano novo para inclusão.</summary>
    /// <param name="plano">Plano.</param>
    Task Adicionar(PlanoDeCobranca plano, CancellationToken ct = default);

    /// <summary>Marca um item novo para inclusão.</summary>
    /// <remarks>
    /// Explícito, e não só pela coleção do plano: o id é gerado na aplicação, e o EF trataria o
    /// item com id preenchido achado na coleção como já existente — um <c>UPDATE</c> que não acha
    /// linha.
    /// </remarks>
    /// <param name="item">Item.</param>
    Task AdicionarItem(ItemDeCobranca item, CancellationToken ct = default);

    /// <summary>Marca um item para remoção.</summary>
    /// <param name="item">Item carregado por <see cref="ObterParaEdicao"/>.</param>
    void RemoverItem(ItemDeCobranca item);
}
