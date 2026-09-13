using Backend.Business.Legal.Interfaces;
using Backend.Business.Legal.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Leitura dos documentos legais e gravação do consentimento.
/// </summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class LegalRepository(AppDbContext db) : ILegalRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Vigente é a versão mais recente <b>já em vigor</b> — uma publicada com data futura só
    /// passa a valer quando a data chega. Desempate por <c>Id</c> (UUIDv7, cresce com o tempo)
    /// para duas publicações com a mesma data não se alternarem entre chamadas.
    /// <para>
    /// A ordenação final é em memória — uma linha por tipo — porque o EF Core não traduz
    /// <c>OrderBy</c> aplicado sobre o <c>First()</c> de cada grupo.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<VersaoDeDocumento>> ListarVigentes(DateTime agora, CancellationToken ct = default)
    {
        var vigentes = await db
            .DocumentosLegais.AsNoTracking()
            .Where(d => d.VigenteDesde <= agora)
            .GroupBy(d => d.Tipo)
            .Select(grupo => grupo.OrderByDescending(d => d.VigenteDesde).ThenByDescending(d => d.Id).First())
            .ToListAsync(ct);

        return
        [
            .. vigentes
                .OrderBy(d => d.Tipo, StringComparer.Ordinal)
                .Select(d => new VersaoDeDocumento(d.Id, d.Tipo, d.Versao, d.Conteudo, d.VigenteDesde)),
        ];
    }

    /// <inheritdoc />
    public Task<VersaoDeDocumento?> ObterVersao(string tipo, string versao, CancellationToken ct = default) =>
        db
            .DocumentosLegais.AsNoTracking()
            .Where(d => d.Tipo == tipo && d.Versao == versao)
            .Select(d => new VersaoDeDocumento(d.Id, d.Tipo, d.Versao, d.Conteudo, d.VigenteDesde))
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConsentimentoDoUsuario>> ListarConsentimentos(Guid usuarioId, CancellationToken ct = default) =>
        await (
            from consentimento in db.Consentimentos.AsNoTracking()
            join documento in db.DocumentosLegais on consentimento.DocumentoLegalId equals documento.Id
            where consentimento.UsuarioId == usuarioId
            orderby consentimento.AceitoEm descending, consentimento.Id descending
            select new ConsentimentoDoUsuario(documento.Tipo, consentimento.Versao, consentimento.AceitoEm, consentimento.Revogado)
        ).ToListAsync(ct);

    /// <inheritdoc />
    public async Task Adicionar(ConsentimentoRegistrado consentimento, CancellationToken ct = default) =>
        await db.Consentimentos.AddAsync(consentimento, ct);
}
