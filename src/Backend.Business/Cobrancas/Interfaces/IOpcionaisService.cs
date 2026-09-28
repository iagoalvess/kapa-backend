using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Cobrancas.Interfaces;

/// <summary>
/// Os opcionais da turma: o que o formando pode pedir só para ele.
/// </summary>
/// <remarks>
/// Lista livre, sem opcionais fixo (P1): a tesouraria cadastra o que a turma vende, com o rótulo
/// dela. O que não é livre é o <see cref="TipoDeCobranca"/> do item — é ele que a Sprint 21 lê para
/// saber se o pagamento emite convite, e adivinhar pelo texto digitado faria "Convite (mesa)" e
/// "convite extra" virarem dois comportamentos por causa de um parêntese.
/// <para>
/// Os opcionais são da turma, não do produto (decisão 6), e moram no plano vigente: é o mesmo
/// <see cref="ItemDeCobranca"/> que a tesouraria já cadastra, marcado como sob demanda.
/// </para>
/// </remarks>
public interface IOpcionaisService
{
    /// <summary>
    /// Os itens que o formando pode pedir hoje — a vitrine.
    /// </summary>
    /// <remarks>
    /// Traz também o que ainda não abriu (<c>AberturaDeVendas</c> no futuro), para o cartão mostrar
    /// a data no lugar do botão. O encerrado e o que passou do prazo ficam de fora.
    /// </remarks>
    Task<Result<IReadOnlyList<Opcional>>> Listar(CancellationToken ct = default);

    /// <summary>Cadastra um item opcional no plano vigente.</summary>
    /// <param name="dados">Item, cota, prazo, estoque, abertura e o vínculo com a festa.</param>
    Task<Result<ItemDeCobrancaDetalhe>> Criar(DadosDoOpcional dados, CancellationToken ct = default);

    /// <summary>
    /// Corrige um item opcional.
    /// </summary>
    /// <remarks>
    /// Com pedido feito, a grade não muda (mesma regra do <c>cobranca.item_em_uso</c>), e reduzir o
    /// estoque abaixo do já reservado devolve 409 <c>cobranca.estoque_menor_que_reservado</c> (P8).
    /// </remarks>
    /// <param name="itemId">Item opcional.</param>
    /// <param name="dados">Dados novos.</param>
    /// <param name="autorId">Quem alterou, para a trilha.</param>
    Task<Result<ItemDeCobrancaDetalhe>> Atualizar(Guid itemId, DadosDoOpcional dados, Guid autorId, CancellationToken ct = default);

    /// <summary>Encerra a venda de um item: para de aceitar pedido, e o que já foi pedido fica.</summary>
    /// <param name="itemId">Item opcional.</param>
    /// <param name="autorId">Quem encerrou, para a trilha.</param>
    Task<Result<ItemDeCobrancaDetalhe>> Encerrar(Guid itemId, Guid autorId, CancellationToken ct = default);

    /// <summary>
    /// Exclui um item opcional que nunca teve pedido.
    /// </summary>
    /// <remarks>
    /// Com pedido, 409 <c>cobranca.item_com_pedido</c> e o caminho é encerrar — a mesma regra do
    /// fornecedor em uso da Sprint 10.
    /// </remarks>
    /// <param name="itemId">Item opcional.</param>
    /// <param name="autorId">Quem excluiu, para a trilha.</param>
    Task<Result> Excluir(Guid itemId, Guid autorId, CancellationToken ct = default);
}
