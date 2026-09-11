using Backend.Business.Admin.Models;

namespace Backend.Business.Admin.Interfaces;

/// <summary>
/// Consultas agregadas do painel administrativo.
/// </summary>
public interface IAdminRepository
{
    /// <summary>Apura os números do painel em uma única ida ao banco.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    Task<ResumoAdmin> ObterResumo(CancellationToken ct = default);

    /// <summary>
    /// Conta os administradores **ativos**.
    /// </summary>
    /// <remarks>
    /// Existe para uma proteção específica: impedir que a última conta de administrador seja
    /// desativada ou rebaixada. Sem ela, um clique deixa o sistema sem ninguém capaz de
    /// gerenciar usuários — e a recuperação exige acesso direto ao banco.
    /// </remarks>
    /// <param name="ct">Token de cancelamento.</param>
    Task<int> ContarAdministradoresAtivos(CancellationToken ct = default);
}
