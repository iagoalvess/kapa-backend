using Backend.Business.Abstractions;
using Backend.Business.Busca.Models;

namespace Backend.Business.Busca.Interfaces;

/// <summary>
/// As consultas da busca do topo.
/// </summary>
/// <remarks>
/// Um método por grupo seria uma ida ao banco por grupo com a mesma decisão de papel repetida cinco
/// vezes. Aqui é uma chamada que devolve tudo o que quem pergunta pode ver.
/// </remarks>
public interface IBuscaRepository
{
    /// <summary>Os primeiros acertos de cada grupo que este papel enxerga.</summary>
    /// <param name="quem">Turma e papel de quem pergunta.</param>
    /// <param name="termo">O que a pessoa digitou, já validado.</param>
    /// <param name="porGrupo">Teto de linhas por grupo.</param>
    Task<BuscaNaTurma> Buscar(QuemBusca quem, string termo, int porGrupo, CancellationToken ct = default);
}

/// <summary>
/// A busca do topo: uma caixa, tudo o que a turma tem.
/// </summary>
public interface IBuscaService
{
    /// <summary>O que casa com o termo, agrupado e recortado pelo papel.</summary>
    /// <param name="quem">Turma e papel de quem pergunta.</param>
    /// <param name="termo">O que a pessoa digitou.</param>
    Task<Result<BuscaNaTurma>> Buscar(QuemBusca quem, string? termo, CancellationToken ct = default);
}
