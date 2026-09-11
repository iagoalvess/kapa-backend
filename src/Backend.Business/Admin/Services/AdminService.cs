using Backend.Business.Abstractions;
using Backend.Business.Admin.Interfaces;
using Backend.Business.Admin.Models;

namespace Backend.Business.Admin.Services;

/// <summary>
/// Operações do painel administrativo.
/// </summary>
/// <param name="adminRepository">Consultas agregadas.</param>
public sealed class AdminService(IAdminRepository adminRepository) : IAdminService
{
    /// <inheritdoc />
    public async Task<Result<ResumoAdmin>> ObterResumo(CancellationToken ct = default) => Result.Ok(await adminRepository.ObterResumo(ct));
}
