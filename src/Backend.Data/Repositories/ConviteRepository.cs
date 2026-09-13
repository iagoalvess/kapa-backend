using Backend.Business.Convites.Interfaces;
using Backend.Business.Convites.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Convites e o registro de quem entrou por eles.
/// </summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class ConviteRepository(AppDbContext db) : IConviteRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// A situação é calculada na projeção final, fora do SQL, pela mesma regra de
    /// <see cref="Convite.StatusEm"/> — duas versões dela divergiriam na primeira mudança.
    /// </remarks>
    public async Task<IReadOnlyList<ConviteResumo>> ListarRecentes(DateTime agoraUtc, int limite, CancellationToken ct = default) =>
        await db
            .Convites.AsNoTracking()
            .OrderByDescending(c => c.CriadoEm)
            .ThenByDescending(c => c.Id)
            .Take(limite)
            .Select(c => new ConviteResumo(
                c.Id,
                c.Email,
                c.Papel,
                c.ExpiraEm,
                c.UsosMaximos,
                c.UsosFeitos,
                Convite.Situacao(c.RevogadoEm, c.UsosMaximos, c.UsosFeitos, c.ExpiraEm, agoraUtc),
                c.CriadoEm
            ))
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<Convite?> ObterParaEdicao(Guid conviteId, CancellationToken ct = default) =>
        db.Convites.FirstOrDefaultAsync(c => c.Id == conviteId, ct);

    /// <inheritdoc />
    public Task<Convite?> ObterPorHashDeTodasAsFormaturas(string tokenHash, CancellationToken ct = default) =>
        db.Convites.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(c => c.TokenHash == tokenHash, ct);

    /// <inheritdoc />
    /// <remarks>
    /// O <c>WHERE</c> repete, em SQL, o "pendente" de <see cref="Convite.Situacao"/>. Tem de ser
    /// assim: é a checagem feita <b>sob a trava da linha</b>, depois de o aceite concorrente já ter
    /// consumido o último uso.
    /// </remarks>
    public async Task<bool> ConsumirUsoDeTodasAsFormaturas(Guid conviteId, DateTime agoraUtc, CancellationToken ct = default) =>
        await db
            .Convites.IgnoreQueryFilters()
            .Where(c => c.Id == conviteId && c.RevogadoEm == null && c.ExpiraEm > agoraUtc && (c.UsosMaximos == null || c.UsosFeitos < c.UsosMaximos))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UsosFeitos, c => c.UsosFeitos + 1).SetProperty(c => c.AtualizadoEm, agoraUtc), ct) == 1;

    /// <inheritdoc />
    public async Task Adicionar(Convite convite, CancellationToken ct = default) => await db.Convites.AddAsync(convite, ct);

    /// <inheritdoc />
    public async Task RegistrarAceite(AceiteDeConvite aceite, CancellationToken ct = default) => await db.AceitesDeConvite.AddAsync(aceite, ct);
}
