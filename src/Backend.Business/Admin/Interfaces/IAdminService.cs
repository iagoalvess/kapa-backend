using Backend.Business.Abstractions;
using Backend.Business.Admin.Models;

namespace Backend.Business.Admin.Interfaces;

/// <summary>
/// Operações do painel administrativo.
/// </summary>
public interface IAdminService
{
    /// <summary>Apura os números do painel.</summary>
    Task<Result<ResumoAdmin>> ObterResumo(CancellationToken ct = default);
}
