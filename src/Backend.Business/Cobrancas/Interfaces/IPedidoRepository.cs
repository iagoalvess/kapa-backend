using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// Pedidos dos opcionais da formatura selecionada.
/// </summary>
/// <remarks>Isolados pelo filtro global: nenhum método recebe a formatura.</remarks>
public interface IPedidoRepository
{
    /// <summary>
    /// O item opcional com a linha travada até o fim da transação.
    /// </summary>
    /// <remarks>
    /// <c>SELECT … FOR UPDATE</c>, como <c>ParcelaRepository.TravarParaBaixa</c>: precisa rodar
    /// dentro de <c>IUnitOfWork.EmTransacaoAsync</c>. É o que faz cinquenta pedidos simultâneos num
    /// item com dez unidades virarem dez confirmados e quarenta "esgotado", em vez de sessenta
    /// unidades vendidas — a checagem do estoque acontece <b>sob a trava</b>, depois de a operação
    /// concorrente já ter consumido a última.
    /// <para>
    /// Volta rastreado de propósito: o contador de reservas é escrito pela própria entidade, e o
    /// <c>SalvarAsync</c> do service o persiste na mesma transação. Um <c>ExecuteUpdateAsync</c>
    /// dispararia na hora e fora do <c>IUnitOfWork</c>, e a reserva commitaria sem o pedido existir.
    /// </para>
    /// </remarks>
    /// <param name="itemId">Item opcional.</param>
    Task<ItemDeCobranca?> TravarItem(Guid itemId, CancellationToken ct = default);

    /// <summary>O pedido do vínculo para o item, rastreado; nulo se ele ainda não pediu.</summary>
    /// <param name="vinculoId">Quem pede.</param>
    /// <param name="itemId">Item opcional.</param>
    Task<Pedido?> ObterParaEdicao(Guid vinculoId, Guid itemId, CancellationToken ct = default);

    /// <summary>Um pedido da turma, rastreado; nulo se não existir aqui.</summary>
    /// <param name="pedidoId">Pedido.</param>
    Task<Pedido?> ObterParaEdicao(Guid pedidoId, CancellationToken ct = default);

    /// <summary>Um pedido da turma, como as telas o mostram; nulo se não existir aqui.</summary>
    /// <param name="pedidoId">Pedido.</param>
    Task<PedidoResumo?> Obter(Guid pedidoId, CancellationToken ct = default);

    /// <summary>Os pedidos de um vínculo, do mais novo para o mais antigo.</summary>
    /// <remarks>Sem paginação: são poucos por pessoa — um por item opcional, no máximo.</remarks>
    /// <param name="vinculoId">Quem pediu.</param>
    Task<IReadOnlyList<PedidoResumo>> ListarDoVinculo(Guid vinculoId, CancellationToken ct = default);

    /// <summary>Uma página dos pedidos da turma.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Item, situação e busca por nome.</param>
    Task<PaginaDe<PedidoResumo>> Listar(PaginacaoRequest paginacao, FiltroDePedidos filtro, CancellationToken ct = default);

    /// <summary>A conta aberta de cada item opcional — a faixa do topo da tela de Pedidos.</summary>
    Task<IReadOnlyList<ResumoDoItemPedido>> Resumir(CancellationToken ct = default);

    /// <summary>Se o item opcional já tem pedido — é o que impede a exclusão dele.</summary>
    /// <param name="itemId">Item opcional.</param>
    Task<bool> ExisteDoItem(Guid itemId, CancellationToken ct = default);

    /// <summary>As parcelas de um pedido, rastreadas para alteração, por número.</summary>
    /// <remarks>
    /// O recorte é <c>(vínculo, item)</c>, e não uma coluna <c>PedidoId</c> na parcela: a decisão 3
    /// garante um pedido por item por formando, então as duas coisas são a mesma — e a parcela
    /// continua sendo uma <see cref="Parcela"/> igual a todas as outras, sem coluna nova que o
    /// extrato, o PIX, a baixa e o balancete teriam de aprender a ignorar.
    /// </remarks>
    /// <param name="pedido">Pedido.</param>
    Task<IReadOnlyList<Parcela>> ListarParcelasParaEdicao(Pedido pedido, CancellationToken ct = default);

    /// <summary>O item opcional de convite extra ainda à venda, o mais antigo; nulo se a turma não vende convite.</summary>
    /// <remarks>É dele que a cortesia desconta a cadeira (Sprint 21, decisão 14).</remarks>
    Task<Guid?> ObterItemDeConviteEmVenda(CancellationToken ct = default);

    /// <summary>
    /// Os pedidos confirmados de convite extra já quitados, com quantos convites válidos cada um tem.
    /// </summary>
    /// <remarks>Quitado: nenhuma parcela em aberto e ao menos uma paga — a mesma regra da baixa.</remarks>
    /// <param name="eventoId">Evento dos convites contados.</param>
    Task<IReadOnlyList<PedidoDeConviteQuitado>> ListarDeConviteQuitados(Guid eventoId, CancellationToken ct = default);

    /// <summary>Quantos pedidos de convite extra têm parcela em aberto vencendo depois do dia (P2.1 da Sprint 21).</summary>
    /// <param name="dia">O dia do fechamento da lista.</param>
    Task<int> ContarDeConviteComParcelaDepoisDe(DateOnly dia, CancellationToken ct = default);

    /// <summary>Marca um pedido novo para inclusão.</summary>
    /// <param name="pedido">Pedido.</param>
    Task Adicionar(Pedido pedido, CancellationToken ct = default);
}
