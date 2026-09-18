using Backend.Business.Abstractions;
using Backend.Business.Festa.Models;

namespace Backend.Business.Festa.Interfaces;

/// <summary>
/// O que a turma está comprando, e quanto falta para pagar por isso.
/// </summary>
/// <remarks>
/// Leitura para todo membro, escrita para a Gestão (decisão 5). Nada aqui cria, altera ou cancela
/// parcela: se o custo da festa passa do que a turma arrecada, a tela mostra — quem resolve é o
/// rateio extraordinário da Sprint 7, na assembleia (decisão 9).
/// </remarks>
public interface IItemDaFestaService
{
    /// <summary>Os itens da festa, na ordem da comissão, com o estado e as somas de cada um.</summary>
    /// <remarks>Materializa os seis itens sugeridos na primeira vez que a turma abre a tela (decisão 12).</remarks>
    Task<Result<IReadOnlyList<ItemDaFestaResumo>>> Listar(CancellationToken ct = default);

    /// <summary>Um item da turma.</summary>
    /// <param name="id">Item.</param>
    Task<Result<ItemDaFestaResumo>> ObterPorId(Guid id, CancellationToken ct = default);

    /// <summary>A meta: custo da festa, arrecadado e quanto dela já foi paga.</summary>
    Task<Result<MetaDaFesta>> ObterMeta(CancellationToken ct = default);

    /// <summary>Cria um item no fim da lista.</summary>
    /// <param name="dados">Título, categoria, o que inclui, rateio e valor.</param>
    Task<Result<ItemDaFestaResumo>> Criar(DadosDoItemDaFesta dados, CancellationToken ct = default);

    /// <summary>Corrige um item.</summary>
    /// <param name="id">Item.</param>
    /// <param name="dados">Dados novos.</param>
    Task<Result<ItemDaFestaResumo>> Atualizar(Guid id, DadosDoItemDaFesta dados, CancellationToken ct = default);

    /// <summary>A turma desistiu: o item sai do custo e fica na lista com o selo.</summary>
    /// <param name="id">Item.</param>
    Task<Result<ItemDaFestaResumo>> Cancelar(Guid id, CancellationToken ct = default);

    /// <summary>Desfaz o cancelamento.</summary>
    /// <param name="id">Item.</param>
    Task<Result<ItemDaFestaResumo>> Reativar(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Exclui um item que nunca teve despesa.
    /// </summary>
    /// <remarks>
    /// Com despesa vinculada, 409 <c>festa.item_em_uso</c>: o caminho é cancelar (decisão 13). É a
    /// mesma forma do <c>financeiro.fornecedor_em_uso</c> da Sprint 10, e pela mesma razão — apagar
    /// levaria junto a origem de um gasto já pago.
    /// </remarks>
    /// <param name="id">Item.</param>
    Task<Result> Excluir(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Os itens da festa da formatura selecionada.
/// </summary>
/// <remarks>Isolados pelo filtro global: nenhum método recebe a formatura.</remarks>
public interface IItemDaFestaRepository
{
    /// <summary>Os itens, na ordem da comissão, já com as somas de despesa de cada um.</summary>
    Task<IReadOnlyList<ItemDaFestaResumo>> Listar(CancellationToken ct = default);

    /// <summary>Um item, como a lista o mostra; nulo se não existir aqui.</summary>
    /// <param name="id">Item.</param>
    Task<ItemDaFestaResumo?> Obter(Guid id, CancellationToken ct = default);

    /// <summary>O item rastreado para alteração; nulo se não existir aqui.</summary>
    /// <param name="id">Item.</param>
    Task<ItemDaFesta?> ObterParaEdicao(Guid id, CancellationToken ct = default);

    /// <summary>Se a turma já tem algum item — é o que decide a materialização dos sugeridos.</summary>
    Task<bool> ExisteAlgum(CancellationToken ct = default);

    /// <summary>A maior posição em uso, ou zero. O item novo entra depois dela.</summary>
    Task<int> UltimaOrdem(CancellationToken ct = default);

    /// <summary>Marca itens novos para inclusão.</summary>
    /// <param name="itens">Itens.</param>
    Task Adicionar(IReadOnlyList<ItemDaFesta> itens, CancellationToken ct = default);

    /// <summary>Marca o item para exclusão.</summary>
    /// <param name="item">Item já carregado.</param>
    void Remover(ItemDaFesta item);
}
