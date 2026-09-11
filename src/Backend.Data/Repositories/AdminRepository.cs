using Backend.Business.Admin.Interfaces;
using Backend.Business.Admin.Models;
using Backend.Business.Usuarios.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Consultas agregadas do painel administrativo.
/// </summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class AdminRepository(AppDbContext db) : IAdminRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// As contagens de usuário saem em uma consulta só (agregação no banco); sessões e
    /// administradores vão em mais duas. Três idas ao banco para uma tela chamada algumas vezes
    /// por dia não justifica a view materializada que a alternativa exigiria.
    /// </remarks>
    public async Task<ResumoAdmin> ObterResumo(CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var limiteDeCadastro = agora.AddDays(-30);

        var usuarios = await db
            .Users.GroupBy(_ => 1)
            .Select(grupo => new
            {
                Total = grupo.LongCount(),
                Ativos = grupo.LongCount(u => u.Ativo),
                Recentes = grupo.LongCount(u => u.CriadoEm >= limiteDeCadastro),
            })
            .FirstOrDefaultAsync(ct);

        var administradores = await ConsultarAdministradoresAtivos().LongCountAsync(ct);

        var sessoesAtivas = await db.RefreshTokens.CountAsync(t => t.RevogadoEm == null && t.ExpiraEm > agora, ct);

        var total = usuarios?.Total ?? 0;
        var ativos = usuarios?.Ativos ?? 0;

        return new ResumoAdmin(total, ativos, total - ativos, administradores, sessoesAtivas, usuarios?.Recentes ?? 0, agora);
    }

    /// <inheritdoc />
    public Task<int> ContarAdministradoresAtivos(CancellationToken ct = default) => ConsultarAdministradoresAtivos().CountAsync(ct);

    private IQueryable<Usuario> ConsultarAdministradoresAtivos() =>
        from usuario in db.Users.AsNoTracking()
        join vinculo in db.UserRoles on usuario.Id equals vinculo.UserId
        join perfil in db.Roles on vinculo.RoleId equals perfil.Id
        where perfil.Name == PerfisPadrao.Administrador && usuario.Ativo
        select usuario;
}
