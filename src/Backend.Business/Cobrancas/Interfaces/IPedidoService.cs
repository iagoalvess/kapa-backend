using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// O pedido do formando: a segunda porta que cria parcela, e a única que não é a adesão.
/// </summary>
/// <remarks>
/// Ela nasce com as mesmas guardas da primeira (decisão 4): vínculo ativo, adesão vigente, turma
/// ativa. Quem não aceitou o termo não tem plano congelado e não passa a dever por um caminho
/// lateral.
/// <para>
/// A parcela gerada aqui é uma <see cref="Parcela"/> igual a todas as outras, com
/// <c>ItemDeCobrancaId</c> preenchido e a mesma chave natural — extrato, QR do PIX, informe, baixa,
/// estorno, régua, caixa, projeção, dashboard, balancete e recibo continuam funcionando sem uma
/// linha nova (decisão 1).
/// </para>
/// </remarks>
public interface IPedidoService
{
    /// <summary>Os pedidos do próprio formando.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">O próprio.</param>
    Task<Result<IReadOnlyList<PedidoResumo>>> ListarMeus(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Os pedidos da turma, uma página por vez.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Item, situação e busca por nome.</param>
    Task<Result<PaginaDe<PedidoResumo>>> Listar(PaginacaoRequest paginacao, FiltroDePedidos filtro, CancellationToken ct = default);

    /// <summary>A conta aberta de cada item opcional — a faixa da tela de Pedidos.</summary>
    Task<Result<IReadOnlyList<ResumoDoItemPedido>>> Resumir(CancellationToken ct = default);

    /// <summary>
    /// Pede, reserva o estoque e grava as parcelas — tudo na mesma transação.
    /// </summary>
    /// <remarks>
    /// Se o formando já tem um pedido daquele item, este método ajusta a quantidade dele: um pedido
    /// por item por formando (decisão 3), e o clique duplo vira um pedido só pelo índice único.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    /// <param name="dados">Item e quantidade absoluta.</param>
    Task<Result<PedidoResumo>> Pedir(Guid formaturaId, Guid usuarioId, DadosDoPedido dados, CancellationToken ct = default);

    /// <summary>
    /// Muda a quantidade de um pedido do próprio formando.
    /// </summary>
    /// <remarks>
    /// A quantidade é <b>absoluta</b>: repetir o mesmo <c>PUT</c> reserva delta zero. Diminuir só
    /// enquanto nenhuma parcela do pedido foi paga — 409 <c>cobranca.pedido_com_parcela_paga</c>.
    /// </remarks>
    /// <param name="pedidoId">Pedido.</param>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede — o pedido de outro responde 404.</param>
    /// <param name="quantidade">Quantidade final.</param>
    Task<Result<PedidoResumo>> Ajustar(Guid pedidoId, Guid formaturaId, Guid usuarioId, int quantidade, CancellationToken ct = default);

    /// <summary>
    /// Cancela um pedido: o estoque volta e as parcelas em aberto são canceladas.
    /// </summary>
    /// <remarks>
    /// Com parcela paga e sem crédito informado, o pedido não some — ele encolhe para o
    /// <c>piso(pago ÷ preço unitário)</c> da P9, e só o excedente volta ao estoque. Com crédito, a
    /// tesouraria está devolvendo o dinheiro: o pedido inteiro cai e o crédito vira uma parcela
    /// negativa dele, que abate o que a pessoa ainda deve (P5).
    /// <para>
    /// Cancelar de novo não devolve estoque outra vez — é o <c>WHERE</c> sobre o status, e o
    /// <c>CHECK (reservados &gt;= 0)</c> por baixo dele.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Quem é da tesouraria sai do <b>vínculo gravado</b>, e não da claim <c>papel</c> do token: ela
    /// é uma fotografia da emissão, e rebaixar alguém precisa valer na requisição seguinte.
    /// </remarks>
    /// <param name="pedidoId">Pedido.</param>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem cancela.</param>
    /// <param name="dados">Crédito a devolver, se houver.</param>
    Task<Result<PedidoResumo>> Cancelar(Guid pedidoId, Guid formaturaId, Guid usuarioId, CancelamentoDePedido dados, CancellationToken ct = default);
}
