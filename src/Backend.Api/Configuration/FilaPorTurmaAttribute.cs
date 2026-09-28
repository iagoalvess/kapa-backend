namespace Backend.Api.Configuration;

/// <summary>
/// Marca a rota cuja escrita disputa uma linha só da turma — o estoque do opcional, o uso do link —, e
/// que por isso entra na fila de <see cref="RateLimitConfig"/> em vez de ir direto ao banco.
/// </summary>
/// <remarks>
/// A trava do banco já serializa essas escritas; a fila só tira a espera de dentro do pool. Sem ela, a
/// abertura de vendas de uma turma segurava as 40 conexões do pool esperando a mesma linha, e a
/// requisição de qualquer outra turma esperava junto (medido em 23/09/2026).
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FilaPorTurmaAttribute : Attribute;
