using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Acesso à tabela de eventos.
/// </summary>
/// <param name="db">Contexto de dados do escopo.</param>
public sealed class EventoRepository(AppDbContext db) : IEventoRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Diferente do resto do projeto, grava e persiste na mesma chamada. Não há o que compor:
    /// quem chama é o serviço de descarga da fila, que já roda fora de qualquer requisição e não
    /// tem outra escrita para agrupar.
    /// </remarks>
    public async Task GravarLote(IReadOnlyList<Evento> eventos, CancellationToken ct = default)
    {
        if (eventos.Count == 0)
            return;

        await db.Eventos.AddRangeAsync(eventos, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public Task<int> RemoverAnterioresA(DateTime limiteUtc, CancellationToken ct = default) =>
        db.Eventos.Where(e => e.OcorridoEm < limiteUtc).ExecuteDeleteAsync(ct);
}
