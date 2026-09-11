using Backend.Business.Abstractions;
using Backend.Business.Auth.Models;
using Backend.Business.Formaturas.Models;

namespace Backend.Business.Formaturas.Interfaces;

/// <summary>
/// Listagem e seleção da formatura da sessão.
/// </summary>
/// <remarks>
/// As duas operações funcionam **sem** formatura selecionada: são justamente o caminho para
/// selecionar uma. Elas verificam o usuário autenticado e o vínculo ativo dele.
/// </remarks>
public interface IFormaturaService
{
    /// <summary>Formaturas em que o usuário tem vínculo ativo.</summary>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<IReadOnlyList<FormaturaDoUsuario>>> ListarMinhas(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Troca a formatura da sessão, devolvendo um par de tokens novo.
    /// </summary>
    /// <remarks>
    /// A formatura entra no token assinado pela API, que só põe ali uma turma cujo vínculo
    /// acabou de ser verificado.
    /// </remarks>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="formaturaId">Formatura pretendida.</param>
    /// <param name="refreshTokenAtual">Refresh token da sessão atual, rotacionado na troca.</param>
    /// <param name="ipDeOrigem">IP do solicitante, registrado para auditoria.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ParDeTokens>> Selecionar(
        Guid usuarioId,
        Guid formaturaId,
        string refreshTokenAtual,
        string? ipDeOrigem,
        CancellationToken ct = default
    );
}
