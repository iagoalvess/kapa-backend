using Backend.Business.Formaturas.Models;

namespace Backend.Business.Formaturas.Interfaces;

/// <summary>
/// Consultas sobre o vínculo entre usuário e formatura.
/// </summary>
public interface IVinculoRepository
{
    /// <summary>Formaturas em que o usuário tem vínculo ativo, com o papel dele em cada uma.</summary>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<IReadOnlyList<FormaturaDoUsuario>> ListarDoUsuario(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// O vínculo ativo do usuário, <b>se</b> ele tiver exatamente um.
    /// </summary>
    /// <remarks>
    /// Nulo tanto para nenhum quanto para dois ou mais: os dois casos precisam de uma decisão
    /// que não é do sistema — criar a primeira formatura, ou escolher entre as que existem.
    /// </remarks>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<VinculoAtivo?> ObterUnicoAtivo(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Papel do usuário na formatura, ou nulo se não houver vínculo ativo.</summary>
    /// <param name="usuarioId">Usuário autenticado.</param>
    /// <param name="formaturaId">Formatura pretendida.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<string?> ObterPapelAtivo(Guid usuarioId, Guid formaturaId, CancellationToken ct = default);

    /// <summary>Registra um vínculo novo.</summary>
    /// <param name="vinculo">Vínculo a persistir.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task Adicionar(VinculoDeFormatura vinculo, CancellationToken ct = default);
}
