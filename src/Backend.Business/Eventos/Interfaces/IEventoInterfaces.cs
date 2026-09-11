using Backend.Business.Eventos.Models;

namespace Backend.Business.Eventos.Interfaces;

/// <summary>
/// Registra um evento de uso.
/// </summary>
/// <remarks>
/// A implementação enfileira em memória e devolve na hora. **Nunca bloqueia e nunca lança** —
/// analytics não pode derrubar nem atrasar a operação que estava sendo medida. Se a fila estiver
/// cheia, o evento é descartado e o descarte é contabilizado.
/// <para>
/// Essa é a diferença entre este canal e o de e-mail: e-mail perdido é problema do usuário,
/// evento perdido é um ponto a menos num gráfico.
/// </para>
/// </remarks>
public interface IRegistradorDeEventos
{
    /// <summary>Registra um evento para gravação assíncrona.</summary>
    /// <param name="evento">Evento a registrar.</param>
    void Registrar(Evento evento);
}

/// <summary>
/// Acesso à tabela de eventos.
/// </summary>
public interface IEventoRepository
{
    /// <summary>
    /// Grava um lote de eventos.
    /// </summary>
    /// <remarks>
    /// Em lote porque evento é barato de produzir e caro de gravar um a um: uma ida ao banco por
    /// clique não escala. A gravação acontece fora do caminho da requisição.
    /// </remarks>
    /// <param name="eventos">Eventos a gravar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task GravarLote(IReadOnlyList<Evento> eventos, CancellationToken ct = default);

    /// <summary>
    /// Apaga eventos anteriores ao limite.
    /// </summary>
    /// <remarks>
    /// Chamado pelo worker de retenção. Sem isso a tabela cresce para sempre — e é a tabela que
    /// mais cresce, porque registra atividade e não estado.
    /// </remarks>
    /// <param name="limiteUtc">Só remove eventos anteriores a este instante.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Quantidade de eventos removidos.</returns>
    Task<int> RemoverAnterioresA(DateTime limiteUtc, CancellationToken ct = default);
}
