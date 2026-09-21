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

    /// <summary>Um item com as propostas levantadas para ele — o painel da direita da tela.</summary>
    /// <param name="id">Item.</param>
    /// <param name="formaturaId">Turma da sessão, para achar o vínculo de quem lê.</param>
    /// <param name="usuarioId">Quem está lendo: é o que decide o <c>meu_voto</c> de cada proposta.</param>
    Task<Result<ItemDaFestaDetalhe>> ObterDetalhe(Guid id, Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

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
/// As propostas de um item e o voto da turma nelas.
/// </summary>
/// <remarks>
/// Escrita para a Gestão, voto para o formando (decisão 17). Proposta e voto só existem enquanto o
/// item está "a contratar": depois de lançada a despesa, a escolha já aconteceu, e reabrir a votação
/// sobre um contrato assinado é discussão que a tela não deve hospedar.
/// </remarks>
public interface IPropostaService
{
    /// <summary>Acrescenta uma candidata ao item.</summary>
    /// <param name="itemId">Item, que precisa estar "a contratar".</param>
    /// <param name="dados">Título, valor e o que inclui.</param>
    Task<Result<PropostaResumo>> Criar(Guid itemId, DadosDaProposta dados, CancellationToken ct = default);

    /// <summary>Corrige uma proposta.</summary>
    /// <param name="id">Proposta.</param>
    /// <param name="dados">Dados novos.</param>
    Task<Result<PropostaResumo>> Atualizar(Guid id, DadosDaProposta dados, CancellationToken ct = default);

    /// <summary>
    /// Tira uma proposta da disputa.
    /// </summary>
    /// <remarks>
    /// Os votos dela vão junto, em cascata: voto é preferência, não dinheiro, e manter voto órfão
    /// obrigaria a inventar um estado "votou em proposta que não existe mais" que ninguém lê.
    /// </remarks>
    /// <param name="id">Proposta.</param>
    Task<Result> Excluir(Guid id, CancellationToken ct = default);

    /// <summary>O formando escolhe uma proposta, ou troca a que já tinha escolhido.</summary>
    /// <param name="propostaId">A escolhida.</param>
    /// <param name="formaturaId">Turma da sessão.</param>
    /// <param name="usuarioId">Quem vota.</param>
    Task<Result> Votar(Guid propostaId, Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Tira o voto do formando naquele item.</summary>
    /// <param name="itemId">Item.</param>
    /// <param name="formaturaId">Turma da sessão.</param>
    /// <param name="usuarioId">Quem vota.</param>
    Task<Result> Desvotar(Guid itemId, Guid formaturaId, Guid usuarioId, CancellationToken ct = default);
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

/// <summary>
/// As propostas dos itens da festa e os votos nelas.
/// </summary>
/// <remarks>Isolados pelo filtro global: nenhum método recebe a formatura.</remarks>
public interface IPropostaRepository
{
    /// <summary>As propostas de um item, da mais votada para a menos, com o voto de quem lê.</summary>
    /// <param name="itemId">Item.</param>
    /// <param name="vinculoId">Quem está lendo; <c>null</c> devolve <c>meu_voto</c> falso em todas.</param>
    Task<IReadOnlyList<PropostaResumo>> Listar(Guid itemId, Guid? vinculoId, CancellationToken ct = default);

    /// <summary>Uma proposta, como a lista a mostra; nula se não existir aqui.</summary>
    /// <param name="id">Proposta.</param>
    /// <param name="vinculoId">Quem está lendo.</param>
    Task<PropostaResumo?> Obter(Guid id, Guid? vinculoId, CancellationToken ct = default);

    /// <summary>A proposta rastreada para alteração; nula se não existir aqui.</summary>
    /// <param name="id">Proposta.</param>
    Task<PropostaDoItem?> ObterParaEdicao(Guid id, CancellationToken ct = default);

    /// <summary>O voto que o vínculo já deu naquele item; nulo se ainda não votou.</summary>
    /// <param name="vinculoId">Quem vota.</param>
    /// <param name="itemId">Item.</param>
    Task<VotoNaProposta?> ObterVoto(Guid vinculoId, Guid itemId, CancellationToken ct = default);

    /// <summary>Marca uma proposta nova para inclusão.</summary>
    /// <param name="proposta">Proposta.</param>
    Task Adicionar(PropostaDoItem proposta, CancellationToken ct = default);

    /// <summary>Marca um voto novo para inclusão.</summary>
    /// <param name="voto">Voto.</param>
    Task AdicionarVoto(VotoNaProposta voto, CancellationToken ct = default);

    /// <summary>Marca a proposta para exclusão; os votos dela vão em cascata.</summary>
    /// <param name="proposta">Proposta já carregada.</param>
    void Remover(PropostaDoItem proposta);

    /// <summary>Marca o voto para exclusão.</summary>
    /// <param name="voto">Voto já carregado.</param>
    void RemoverVoto(VotoNaProposta voto);
}
