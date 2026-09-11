using Backend.Business.Auth.Models;

namespace Backend.Business.Auth.Interfaces;

/// <summary>
/// Acesso a dados dos refresh tokens.
/// </summary>
/// <remarks>Nenhum método persiste; o commit é do service, via <c>IUnitOfWork</c>.</remarks>
public interface IRefreshTokenRepository
{
    /// <summary>Marca um token para inclusão.</summary>
    /// <param name="token">Token a incluir.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task Adicionar(RefreshToken token, CancellationToken ct = default);

    /// <summary>Busca um token pelo hash, rastreado para alteração.</summary>
    /// <param name="hash">Hash SHA-256 do token apresentado.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<RefreshToken?> ObterPorHash(string hash, CancellationToken ct = default);

    /// <summary>
    /// Revoga todos os tokens ativos de um usuário. Usado no logout de todas as sessões,
    /// na desativação da conta e na detecção de reúso.
    /// </summary>
    /// <param name="usuarioId">Dono dos tokens.</param>
    /// <param name="agoraUtc">Momento a registrar como revogação.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task RevogarTodosDoUsuario(Guid usuarioId, DateTime agoraUtc, CancellationToken ct = default);

    /// <summary>
    /// Apaga tokens expirados ou revogados antes do limite. Chamado pelo worker de limpeza —
    /// sem isso a tabela cresce para sempre.
    /// </summary>
    /// <param name="limiteUtc">Só remove registros anteriores a este instante.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Quantidade de registros removidos.</returns>
    Task<int> RemoverInativosAnterioresA(DateTime limiteUtc, CancellationToken ct = default);
}
