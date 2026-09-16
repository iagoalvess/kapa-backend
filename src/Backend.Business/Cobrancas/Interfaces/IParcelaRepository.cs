using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// Parcelas da formatura selecionada.
/// </summary>
/// <remarks>Isoladas pelo filtro global: nenhum método recebe a formatura.</remarks>
public interface IParcelaRepository
{
    /// <summary>Os números já gerados do item para o vínculo — o que a geração pula.</summary>
    /// <param name="vinculoId">Vínculo de quem deve.</param>
    /// <param name="itemId">Item de origem.</param>
    Task<IReadOnlyList<int>> ListarNumerosGerados(Guid vinculoId, Guid itemId, CancellationToken ct = default);

    /// <summary>Quais itens do plano já geraram parcela.</summary>
    /// <param name="planoId">Plano.</param>
    Task<IReadOnlyList<Guid>> ListarItensEmUso(Guid planoId, CancellationToken ct = default);

    /// <summary>Quantos vínculos têm parcela de algum item do plano — quem aderiu a ele.</summary>
    /// <param name="planoId">Plano.</param>
    Task<int> ContarVinculosComParcela(Guid planoId, CancellationToken ct = default);

    /// <summary>Se o item já gerou alguma parcela.</summary>
    /// <param name="itemId">Item.</param>
    Task<bool> ExisteDoItem(Guid itemId, CancellationToken ct = default);

    /// <summary>As parcelas abertas do item que vencem a partir do dia, rastreadas para alteração.</summary>
    /// <param name="itemId">Item.</param>
    /// <param name="aPartirDe">Primeiro vencimento incluído — tipicamente hoje.</param>
    Task<IReadOnlyList<Parcela>> ListarAbertasParaEdicao(Guid itemId, DateOnly aPartirDe, CancellationToken ct = default);

    /// <summary>Uma página das parcelas da turma, por vencimento.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Formando, situação e período.</param>
    /// <param name="hoje">Dia que separa aberta de vencida.</param>
    Task<PaginaDe<ParcelaResumo>> Listar(PaginacaoRequest paginacao, FiltroDeParcelas filtro, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Uma parcela da turma, como a lista a mostra, sem o valor do dia; nula se não existir aqui.</summary>
    /// <param name="parcelaId">Parcela.</param>
    /// <param name="hoje">Dia que separa aberta de vencida.</param>
    Task<ParcelaResumo?> Obter(Guid parcelaId, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Todas as parcelas de um vínculo, por vencimento, sem o valor do dia — o extrato.</summary>
    /// <remarks>Sem paginação: um formando tem algumas dezenas de parcelas, e o extrato mostra todas.</remarks>
    /// <param name="vinculoId">Vínculo do formando.</param>
    /// <param name="hoje">Dia que separa aberta de vencida.</param>
    Task<IReadOnlyList<ParcelaResumo>> ListarDoVinculo(Guid vinculoId, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Quantas parcelas e quanto somam, por situação do dia, numa consulta agrupada.</summary>
    /// <param name="filtro">Formando, período e busca; a situação é ignorada.</param>
    /// <param name="hoje">Dia que separa aberta de vencida.</param>
    Task<IReadOnlyList<ContagemDeParcelas>> Contar(FiltroDeParcelas filtro, DateOnly hoje, CancellationToken ct = default);

    /// <summary>As vencidas do filtro, com o que o valor do dia precisa.</summary>
    /// <param name="filtro">Formando, período e busca; a situação é ignorada.</param>
    /// <param name="hoje">Dia que separa aberta de vencida.</param>
    Task<IReadOnlyList<ParcelaEmAtraso>> ListarEmAtraso(FiltroDeParcelas filtro, DateOnly hoje, CancellationToken ct = default);

    /// <summary>
    /// As regras de atraso que cada vínculo aceitou, pela adesão mais recente dele.
    /// </summary>
    /// <remarks>
    /// Do snapshot, e não do plano atual: a tesouraria pode mudar o plano, e quem aderiu fica com o que
    /// aceitou. Vínculo sem adesão fica de fora — e quem lê usa <see cref="RegrasDeAtraso.Nenhuma"/>.
    /// </remarks>
    /// <param name="vinculoIds">Vínculos.</param>
    Task<IReadOnlyDictionary<Guid, RegrasDeAtraso>> ObterRegrasDeAtraso(IReadOnlyCollection<Guid> vinculoIds, CancellationToken ct = default);

    /// <summary>
    /// As parcelas, rastreadas e travadas até o fim da transação.
    /// </summary>
    /// <remarks>
    /// <c>SELECT … FOR UPDATE</c>: precisa rodar dentro de <c>IUnitOfWork.EmTransacaoAsync</c>. É o que
    /// faz duas confirmações simultâneas da mesma parcela virarem uma baixa e um "já estava paga", em vez
    /// de um erro de índice.
    /// </remarks>
    /// <param name="parcelaIds">Parcelas.</param>
    Task<IReadOnlyList<Parcela>> TravarParaBaixa(IReadOnlyCollection<Guid> parcelaIds, CancellationToken ct = default);

    /// <summary>Marca parcelas novas para inclusão.</summary>
    /// <param name="parcelas">Parcelas.</param>
    Task Adicionar(IReadOnlyList<Parcela> parcelas, CancellationToken ct = default);
}
