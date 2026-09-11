using Backend.Business.Auth.Interfaces;
using Backend.Business.Auth.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Persistência dos refresh tokens sobre o EF Core.
/// </summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    /// <inheritdoc />
    public async Task Adicionar(RefreshToken token, CancellationToken ct = default) => await db.RefreshTokens.AddAsync(token, ct);

    /// <inheritdoc />
    /// <remarks>Rastreado de propósito: quem busca por hash é a rotação, que vai alterar o registro.</remarks>
    public Task<RefreshToken?> ObterPorHash(string hash, CancellationToken ct = default) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Carrega e marca em vez de usar <c>ExecuteUpdate</c>: assim a revogação entra na mesma
    /// transação da operação que a disparou (desativar conta, detectar reúso) e desfaz junto se
    /// aquela falhar. São poucas linhas por usuário — não vale trocar consistência por I/O.
    /// </remarks>
    public async Task RevogarTodosDoUsuario(Guid usuarioId, DateTime agoraUtc, CancellationToken ct = default)
    {
        var ativos = await db.RefreshTokens.Where(t => t.UsuarioId == usuarioId && t.RevogadoEm == null).ToListAsync(ct);

        foreach (var token in ativos)
            token.RevogadoEm = agoraUtc;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Esta é a exceção à regra do <c>IUnitOfWork</c>: <c>ExecuteDeleteAsync</c> emite o
    /// <c>DELETE</c> na hora, sem passar pelo rastreador. É limpeza em massa disparada pelo
    /// worker, sem nada para compor — carregar milhares de entidades só para descartá-las
    /// seria desperdício.
    /// </remarks>
    public Task<int> RemoverInativosAnterioresA(DateTime limiteUtc, CancellationToken ct = default) =>
        db.RefreshTokens.Where(t => t.ExpiraEm < limiteUtc || (t.RevogadoEm != null && t.RevogadoEm < limiteUtc)).ExecuteDeleteAsync(ct);
}
