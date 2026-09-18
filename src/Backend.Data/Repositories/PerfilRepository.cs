using Backend.Business.Formandos.Interfaces;
using Backend.Business.Formandos.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Cadastros dos formandos da formatura selecionada.
/// </summary>
/// <remarks>
/// O perfil é isolado pelo filtro global; o vínculo, não — por isso toda consulta que parte do
/// vínculo leva a formatura explícita, como em <see cref="VinculoRepository"/>.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class PerfilRepository(AppDbContext db) : IPerfilRepository
{
    /// <inheritdoc />
    public Task<MembroDoPerfil?> ObterMembro(Guid formaturaId, Guid usuarioId, CancellationToken ct = default) =>
        (
            from vinculo in db.Vinculos.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            where vinculo.FormaturaId == formaturaId && vinculo.UsuarioId == usuarioId && vinculo.Ativo
            select new MembroDoPerfil(vinculo.Id, usuario.Id, usuario.Nome, usuario.Email ?? string.Empty, vinculo.Papel)
        ).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<MembroDoPerfil?> ObterTitular(Guid formaturaId, Guid usuarioId, CancellationToken ct = default) =>
        (
            from vinculo in db.Vinculos.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            where vinculo.FormaturaId == formaturaId && vinculo.UsuarioId == usuarioId && (vinculo.Ativo || vinculo.DesligadoEm != null)
            select new MembroDoPerfil(vinculo.Id, usuario.Id, usuario.Nome, usuario.Email ?? string.Empty, vinculo.Papel)
        ).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<PerfilDoFormando?> ObterDoVinculo(Guid vinculoId, CancellationToken ct = default) =>
        db.PerfisDeFormandos.AsNoTracking().FirstOrDefaultAsync(p => p.VinculoId == vinculoId, ct);

    /// <inheritdoc />
    public Task<PerfilDoFormando?> ObterParaEdicao(Guid vinculoId, CancellationToken ct = default) =>
        db.PerfisDeFormandos.FirstOrDefaultAsync(p => p.VinculoId == vinculoId, ct);

    /// <inheritdoc />
    public async Task Adicionar(PerfilDoFormando perfil, CancellationToken ct = default) => await db.PerfisDeFormandos.AddAsync(perfil, ct);

    /// <inheritdoc />
    public async Task RegistrarCorrecao(CorrecaoDePerfil correcao, CancellationToken ct = default) =>
        await db.CorrecoesDePerfil.AddAsync(correcao, ct);
}
