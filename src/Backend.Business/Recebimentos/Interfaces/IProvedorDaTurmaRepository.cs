using Backend.Business.Recebimentos.Models;

namespace Backend.Business.Recebimentos.Interfaces;

/// <summary>Uma cobrança que a conciliação precisa consultar, e de qual turma ela é.</summary>
/// <param name="CobrancaId">Cobrança.</param>
/// <param name="FormaturaId">Turma, para o job apontar o escopo.</param>
public sealed record CobrancaAConciliar(Guid CobrancaId, Guid FormaturaId);

/// <summary>
/// O Mercado Pago da turma: a autorização (credencial) e as cobranças emitidas com ela.
/// </summary>
/// <remarks>
/// Os métodos sem sufixo enxergam só a formatura do escopo. Os <c>DeTodasAsFormaturas</c> existem para
/// quem chega sem turma — o aviso de pagamento, o job — e só devolvem o bastante para apontar o escopo.
/// </remarks>
public interface IProvedorDaTurmaRepository
{
    /// <summary>A conexão da turma, com o nome de quem autorizou; nulo se ela não conectou.</summary>
    Task<ProvedorConectado?> ObterConexao(CancellationToken ct = default);

    /// <summary>A credencial da turma, rastreada; nula se ela não conectou.</summary>
    Task<CredencialDeProvedor?> ObterCredencial(CancellationToken ct = default);

    /// <summary>Registra a primeira credencial da turma.</summary>
    Task AdicionarCredencial(CredencialDeProvedor credencial, CancellationToken ct = default);

    /// <summary>Marca a credencial para remoção — desconectar.</summary>
    void RemoverCredencial(CredencialDeProvedor credencial);

    /// <summary>As turmas cuja autorização vence até <paramref name="ate"/>, para o worker renovar.</summary>
    /// <param name="ate">Limite, em UTC.</param>
    /// <param name="limite">Teto por rodada.</param>
    Task<IReadOnlyList<Guid>> ListarFormaturasComCredencialAVencerDeTodasAsFormaturas(DateTime ate, int limite, CancellationToken ct = default);

    /// <summary>
    /// Reserva a emissão de uma cobrança: grava em <c>Emitindo</c> se não houver outra viva com a mesma
    /// chave, e persiste na hora.
    /// </summary>
    /// <remarks>
    /// <c>INSERT … ON CONFLICT DO NOTHING</c> contra o índice único das vivas: a segunda aba do mesmo
    /// formando perde a corrida sem erro, e lê a da primeira. Persiste sozinho — a exceção que o
    /// <c>ConviteDoEventoRepository.EmitirDoPedido</c> já abriu, pelo mesmo motivo: ler antes de gravar
    /// abriria a janela que o índice fecha. E precisa estar gravada <b>antes</b> da chamada ao Mercado
    /// Pago (decisão 12a).
    /// </remarks>
    /// <param name="cobranca">A cobrança nova, em <c>Emitindo</c>.</param>
    /// <returns>Se reservou; falso quando já havia uma viva com a mesma chave.</returns>
    Task<bool> ReservarEmissao(CobrancaBancaria cobranca, CancellationToken ct = default);

    /// <summary>A cobrança viva com esta chave, se houver — emitindo ou emitida.</summary>
    /// <param name="chave">A chave (<see cref="CobrancaBancaria.ChaveDoPix"/>, <see cref="CobrancaBancaria.ChaveDaCompra"/>).</param>
    Task<CobrancaBancaria?> ObterViva(string chave, CancellationToken ct = default);

    /// <summary>Uma cobrança, só para leitura.</summary>
    Task<CobrancaBancaria?> ObterCobranca(Guid id, CancellationToken ct = default);

    /// <summary>Uma cobrança, rastreada para alteração.</summary>
    Task<CobrancaBancaria?> ObterCobrancaParaEdicao(Guid id, CancellationToken ct = default);

    /// <summary>Trava a linha da cobrança até o fim da transação — a baixa que chega por dois caminhos.</summary>
    Task<CobrancaBancaria?> TravarCobranca(Guid id, CancellationToken ct = default);

    /// <summary>De qual cobrança, e de qual turma, é um pedido do Mercado Pago — o aviso chega sem turma.</summary>
    /// <param name="idExterno">Id do pedido lá.</param>
    Task<CobrancaAConciliar?> ObterPorIdExternoDeTodasAsFormaturas(string idExterno, CancellationToken ct = default);

    /// <summary>
    /// As cobranças emitidas e ainda não resolvidas, a consultar no Mercado Pago (decisão 12b).
    /// </summary>
    /// <param name="emitidasAntesDe">Só as emitidas antes disto: a recém-emitida ainda não foi paga.</param>
    /// <param name="limite">Teto por rodada.</param>
    Task<IReadOnlyList<CobrancaAConciliar>> ListarAConciliarDeTodasAsFormaturas(DateTime emitidasAntesDe, int limite, CancellationToken ct = default);
}
