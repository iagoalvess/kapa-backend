using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Models;

namespace Backend.Business.Adesoes.Interfaces;

/// <summary>
/// O resumo do termo por IA (Sprint 24). Chamado pelo worker, nunca por uma requisição.
/// </summary>
/// <remarks>
/// Fica no <c>Business</c>, e não dentro do job, pelo mesmo motivo do <c>IReguaService</c>: o job abre
/// o escopo, marca a hora e registra o log; a regra é aqui, e é aqui que o teste unitário a alcança.
/// </remarks>
public interface IResumoDoTermoService
{
    /// <summary>
    /// As versões que a rodada vai resumir: vigentes, publicadas nos últimos sete dias, sem resumo, no
    /// máximo <c>PorRodada</c>.
    /// </summary>
    /// <remarks>Com a feature desligada (chave vazia), devolve vazio sem consultar o banco.</remarks>
    /// <param name="agoraUtc">Momento da rodada, em UTC.</param>
    Task<IReadOnlyList<TermoSemResumo>> ListarPendentes(DateTime agoraUtc, CancellationToken ct = default);

    /// <summary>
    /// Gera e grava o resumo de uma versão. O escopo já está apontado para a turma dela.
    /// </summary>
    /// <remarks>
    /// Nenhum modelo respondeu, ou a resposta passou do teto: falha, nada é gravado, e a próxima
    /// rodada tenta de novo.
    /// </remarks>
    /// <param name="termoId">Versão a resumir.</param>
    Task<Result> Gerar(Guid termoId, CancellationToken ct = default);
}
