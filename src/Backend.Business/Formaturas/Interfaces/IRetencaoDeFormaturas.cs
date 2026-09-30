using Backend.Business.Formaturas.Models;

namespace Backend.Business.Formaturas.Interfaces;

/// <summary>
/// O fim da vida de uma turma: encerrar a abandonada e eliminar a que passou do prazo de guarda.
/// </summary>
/// <remarks>
/// Prazos decididos em 25/09/2026: suspensa há 12 meses encerra; encerrada há 5 anos (a prescrição
/// das cobranças e a guarda fiscal, e o que a tela de encerrar promete) é eliminada; descartada, que
/// nunca contratou, é eliminada em 30 dias.
/// </remarks>
public interface IRetencaoDeFormaturasService
{
    /// <summary>As turmas suspensas há mais de 12 meses — cada uma se encerra no seu escopo (<see cref="EncerrarAbandonada"/>).</summary>
    Task<IReadOnlyList<Guid>> ListarSuspensasAbandonadas(CancellationToken ct = default);

    /// <summary>
    /// Encerra a turma suspensa há mais de 12 meses, mesmo com pendências, e registra na auditoria quais ficaram
    /// abertas (Sprint 42, decisão 7). O prazo de 5 anos começa daí.
    /// </summary>
    /// <remarks>
    /// Quem chama aponta o escopo para a turma: as pendências são contadas pelo filtro global, como no encerramento
    /// pela comissão. Confere o prazo de novo — a turma reativada no meio do caminho fica.
    /// </remarks>
    /// <param name="formaturaId">Turma.</param>
    /// <returns>Se foi encerrada agora.</returns>
    Task<bool> EncerrarAbandonada(Guid formaturaId, CancellationToken ct = default);

    /// <summary>As turmas cujo prazo de guarda venceu e que ainda não foram eliminadas.</summary>
    Task<IReadOnlyList<Guid>> ListarParaEliminar(CancellationToken ct = default);

    /// <summary>
    /// Apaga os arquivos e os dados da turma, menos o que é prova.
    /// </summary>
    /// <remarks>
    /// Quem chama aponta o escopo para a turma (<c>FormaturaDoProcessamento</c>): o filtro global é o
    /// que garante que só as linhas dela saem. Confere o prazo de novo e é idempotente — rodar duas
    /// vezes não apaga nada a mais.
    /// </remarks>
    /// <param name="formaturaId">Turma a eliminar.</param>
    /// <returns>Se a turma foi eliminada agora.</returns>
    Task<bool> Eliminar(Guid formaturaId, CancellationToken ct = default);
}

/// <summary>
/// Consultas e a remoção em massa da retenção de turmas.
/// </summary>
public interface IRetencaoDeFormaturasRepository
{
    /// <summary>Turmas suspensas desde antes do limite, das mais antigas.</summary>
    /// <param name="suspensasAte">Suspensas antes deste instante, em UTC.</param>
    /// <param name="limite">Máximo por chamada.</param>
    Task<IReadOnlyList<Guid>> ListarSuspensasAnterioresA(DateTime suspensasAte, int limite, CancellationToken ct = default);

    /// <summary>Turmas não eliminadas cujo prazo venceu.</summary>
    /// <param name="encerradasAte">Encerradas antes deste instante, em UTC.</param>
    /// <param name="descartadasAte">Descartadas antes deste instante, em UTC.</param>
    /// <param name="limite">Máximo por chamada.</param>
    Task<IReadOnlyList<Guid>> ListarParaEliminar(DateTime encerradasAte, DateTime descartadasAte, int limite, CancellationToken ct = default);

    /// <summary>
    /// Apaga as linhas da turma e desativa os vínculos, na transação de quem chama.
    /// </summary>
    /// <remarks>
    /// Persiste por conta própria (<c>ExecuteDeleteAsync</c>): são dezenas de tabelas, sem nada a
    /// compor, e carregar cada linha para marcá-la seria ler a turma inteira só para apagá-la.
    /// </remarks>
    /// <param name="formaturaId">Turma a eliminar.</param>
    /// <param name="prefixoDosArquivos">Prefixo das chaves dos arquivos da turma.</param>
    Task ApagarDados(Guid formaturaId, string prefixoDosArquivos, CancellationToken ct = default);
}
