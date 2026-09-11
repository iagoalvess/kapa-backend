using Backend.Business.Abstractions;
using Backend.Business.Usuarios.Interfaces;
using Backend.Business.Usuarios.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Consultas de usuário sobre o EF Core.
/// </summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class UsuarioRepository(AppDbContext db) : IUsuarioRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// A ordenação inclui o <c>Id</c> como desempate. Sem um critério único, dois registros de
    /// mesmo nome podem trocar de posição entre uma página e outra, fazendo um item sumir da
    /// listagem e outro aparecer duas vezes.
    /// </remarks>
    public async Task<PaginaDe<UsuarioResumo>> Listar(PaginacaoRequest paginacao, string? busca, CancellationToken ct = default)
    {
        var consulta = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = $"%{busca.Trim()}%";
            consulta = consulta.Where(u => EF.Functions.ILike(u.Nome, termo) || EF.Functions.ILike(u.Email!, termo));
        }

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<UsuarioResumo>.Vazia(paginacao);

        var itens = await consulta
            .OrderBy(u => u.Nome)
            .ThenBy(u => u.Id)
            .Skip(paginacao.Pular)
            .Take(paginacao.Tamanho)
            .Select(u => new UsuarioResumo(u.Id, u.Nome, u.Email!, u.Ativo, u.CriadoEm))
            .ToListAsync(ct);

        return new PaginaDe<UsuarioResumo>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    public async Task<UsuarioDetalhe?> ObterDetalhe(Guid id, CancellationToken ct = default)
    {
        var usuario = await db
            .Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id,
                u.Nome,
                u.Email,
                u.EmailConfirmed,
                u.Ativo,
                u.CriadoEm,
                u.AtualizadoEm,
            })
            .FirstOrDefaultAsync(ct);

        if (usuario is null)
            return null;

        var perfis = await (
            from vinculo in db.UserRoles.AsNoTracking()
            join perfil in db.Roles.AsNoTracking() on vinculo.RoleId equals perfil.Id
            where vinculo.UserId == id
            orderby perfil.Name
            select perfil.Name!
        ).ToListAsync(ct);

        return new UsuarioDetalhe(
            usuario.Id,
            usuario.Nome,
            usuario.Email ?? string.Empty,
            usuario.EmailConfirmed,
            usuario.Ativo,
            perfis,
            usuario.CriadoEm,
            usuario.AtualizadoEm
        );
    }

    /// <inheritdoc />
    public Task<Usuario?> ObterParaEdicao(Guid id, CancellationToken ct = default) => db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
}
