using Backend.Business.Formaturas.Models;

namespace Backend.Business.Formaturas.Interfaces;

/// <summary>
/// O que ainda está em aberto na turma — as portas de saída perguntam antes de gravar (Sprint 38, decisão 7).
/// </summary>
/// <remarks>
/// Contagens, não travas: a corrida com quem compra no mesmo segundo é inofensiva, porque a compra nova esbarra
/// na turma já encerrada ou no evento já cancelado. Uma consulta por pergunta, atravessando loja, pedidos,
/// parcelas e cobranças — isoladas pelo filtro global da formatura.
/// </remarks>
public interface IPendenciasDaTurmaRepository
{
    /// <summary>As vendas de pé da festa: compras da loja não canceladas e pedidos de convite confirmados (P10).</summary>
    Task<VendasDaFesta> ContarVendasDaFesta(CancellationToken ct = default);

    /// <summary>Tudo o que impede encerrar a turma (P11).</summary>
    /// <param name="agora">Instante, em UTC — a cobrança que já venceu não está mais viva.</param>
    Task<PendenciasDaTurma> ContarParaEncerrar(DateTime agora, CancellationToken ct = default);
}
