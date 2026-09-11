using Backend.Business.Abstractions;
using Backend.Business.Admin.Models;

namespace Backend.Business.Admin.Interfaces;

/// <summary>
/// Operações do painel administrativo.
/// </summary>
public interface IAdminService
{
    /// <summary>Apura os números do painel.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ResumoAdmin>> ObterResumo(CancellationToken ct = default);
}
