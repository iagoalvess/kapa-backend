using Backend.Business.Abstractions;
using Backend.Business.Festa.Models;

namespace Backend.Business.Festa.Interfaces;

/// <summary>
/// As mesas do jantar: a comissão cadastra e atribui a mesa vendida a quem a comprou (P1 e P2).
/// </summary>
public interface IMesaService
{
    /// <summary>A faixa do topo, as mesas e os compradores — a tela inteira da Gestão.</summary>
    Task<Result<MapaDeMesas>> Mapa(CancellationToken ct = default);

    /// <summary>As mesas do próprio formando, só para ler.</summary>
    /// <param name="formaturaId">Turma da sessão.</param>
    /// <param name="usuarioId">O próprio.</param>
    Task<Result<IReadOnlyList<MesaResumo>>> ListarMinhas(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Cadastra uma mesa.</summary>
    /// <param name="dados">Identificação, lugares, observação e reserva.</param>
    Task<Result<MesaResumo>> Criar(DadosDaMesa dados, CancellationToken ct = default);

    /// <summary>Altera o cadastro. Marcar como reservada uma mesa com dono é 409 <c>festa.mesa_reservada</c>.</summary>
    /// <param name="id">Mesa.</param>
    /// <param name="dados">Dados novos.</param>
    Task<Result<MesaResumo>> Atualizar(Guid id, DadosDaMesa dados, CancellationToken ct = default);

    /// <summary>Exclui uma mesa sem dono; com dono, 409 <c>festa.mesa_com_dono</c>.</summary>
    /// <param name="id">Mesa.</param>
    Task<Result> Excluir(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Atribui a mesa a quem a comprou, ou a solta (decisão 6).
    /// </summary>
    /// <remarks>
    /// Mais mesas do que a quantidade confirmada nos pedidos de mesa do formando é 409
    /// <c>festa.mesas_alem_do_pedido</c>; mesa reservada, 409 <c>festa.mesa_reservada</c>.
    /// </remarks>
    /// <param name="id">Mesa.</param>
    /// <param name="vinculoId">Formando; nulo solta a mesa.</param>
    Task<Result<MesaResumo>> DefinirDono(Guid id, Guid? vinculoId, CancellationToken ct = default);
}

/// <summary>As mesas da formatura selecionada, e o direito de cada formando a elas.</summary>
/// <remarks>Isoladas pelo filtro global: nenhum método recebe a formatura.</remarks>
public interface IMesaRepository
{
    /// <summary>
    /// As mesas com o nome do dono, numa consulta só, por identificação.
    /// </summary>
    /// <param name="vinculoId">Só as deste dono; nulo, todas.</param>
    Task<IReadOnlyList<MesaResumo>> Listar(Guid? vinculoId = null, CancellationToken ct = default);

    /// <summary>Uma mesa como a lista a mostra; nula se não existir aqui.</summary>
    /// <param name="id">Mesa.</param>
    Task<MesaResumo?> Obter(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Quem tem pedido confirmado do opcional <c>Mesa</c>, com quantas comprou e quantas já tem.
    /// </summary>
    Task<IReadOnlyList<CompradorDeMesa>> ListarCompradores(CancellationToken ct = default);

    /// <summary>Quantas mesas o formando comprou: a soma da quantidade dos pedidos confirmados de mesa.</summary>
    /// <param name="vinculoId">Formando.</param>
    Task<int> Compradas(Guid vinculoId, CancellationToken ct = default);

    /// <summary>
    /// Serializa as atribuições de um mesmo formando até o fim da transação.
    /// </summary>
    /// <remarks>
    /// Contar as mesas dele e gravar a nova precisam acontecer sob a mesma trava, senão duas pessoas
    /// da comissão atribuindo ao mesmo formando no mesmo segundo leem "1 de 2" e dão três.
    /// </remarks>
    /// <param name="vinculoId">Formando.</param>
    Task TravarDono(Guid vinculoId, CancellationToken ct = default);

    /// <summary>Uma mesa rastreada para alteração; nula se não existir aqui.</summary>
    /// <param name="id">Mesa.</param>
    Task<Mesa?> ObterParaEdicao(Guid id, CancellationToken ct = default);

    /// <summary>As mesas do dono, rastreadas, da atribuída por último para a primeira.</summary>
    /// <param name="vinculoId">Formando.</param>
    Task<IReadOnlyList<Mesa>> ListarDoDonoParaEdicao(Guid vinculoId, CancellationToken ct = default);

    /// <summary>Se outra mesa da turma já tem esta identificação, sem diferenciar caixa.</summary>
    /// <param name="identificacao">"Mesa 12".</param>
    /// <param name="exceto">A própria mesa, na edição.</param>
    Task<bool> IdentificacaoEmUso(string identificacao, Guid? exceto, CancellationToken ct = default);

    /// <summary>Marca uma mesa nova para inclusão.</summary>
    /// <param name="mesa">Mesa.</param>
    Task Adicionar(Mesa mesa, CancellationToken ct = default);

    /// <summary>Marca a mesa para exclusão.</summary>
    /// <param name="mesa">Mesa rastreada.</param>
    void Remover(Mesa mesa);
}
