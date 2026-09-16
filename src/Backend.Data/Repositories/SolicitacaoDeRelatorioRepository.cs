using Backend.Business.Relatorios.Interfaces;
using Backend.Business.Relatorios.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// A fila de relatórios pesados.
/// </summary>
/// <remarks>
/// Os dois métodos com sufixo <c>DeTodasAsFormaturas</c> são do worker, que roda sem formatura na
/// sessão: são a saída de emergência documentada do isolamento, e ela mora aqui, não num service.
/// Quem os consome reabre um escopo apontado para a turma antes de ler qualquer dado dela.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class SolicitacaoDeRelatorioRepository(AppDbContext db) : ISolicitacaoDeRelatorioRepository
{
    /// <inheritdoc />
    public async Task Adicionar(SolicitacaoDeRelatorio solicitacao, CancellationToken ct = default) =>
        await db.SolicitacoesDeRelatorio.AddAsync(solicitacao, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SolicitacaoDeRelatorio>> Listar(int quantidade, CancellationToken ct = default) =>
        await db.SolicitacoesDeRelatorio.AsNoTracking().OrderByDescending(s => s.CriadoEm).Take(quantidade).ToListAsync(ct);

    /// <inheritdoc />
    public Task<SolicitacaoDeRelatorio?> Obter(Guid id, CancellationToken ct = default) =>
        db.SolicitacoesDeRelatorio.FirstOrDefaultAsync(s => s.Id == id, ct);

    /// <inheritdoc />
    public Task<SolicitacaoDeRelatorio?> ObterNaFila(TipoDeRelatorio tipo, FiltroDoRelatorio filtro, CancellationToken ct = default) =>
        db
            .SolicitacoesDeRelatorio.AsNoTracking()
            .FirstOrDefaultAsync(
                s =>
                    s.Tipo == tipo
                    && s.Status == StatusDaSolicitacao.NaFila
                    && s.De == filtro.Periodo.De
                    && s.Ate == filtro.Periodo.Ate
                    && s.FornecedorId == filtro.FornecedorId
                    && s.Categoria == filtro.Categoria
                    && s.SituacaoDaDespesa == filtro.SituacaoDaDespesa
                    && s.FormandoId == filtro.FormandoId
                    && s.ItemDeCobrancaId == filtro.ItemDeCobrancaId
                    && s.SituacaoDaParcela == filtro.SituacaoDaParcela,
                ct
            );

    /// <inheritdoc />
    /// <remarks>
    /// Rastreadas: o job conta a tentativa e grava o resultado nelas. Da mais antiga, para que um pedido
    /// não fique para trás enquanto a turma vizinha pede o quarto balancete do dia.
    /// </remarks>
    public async Task<IReadOnlyList<SolicitacaoDeRelatorio>> ListarNaFilaDeTodasAsFormaturas(int quantidade, CancellationToken ct = default) =>
        await db
            .SolicitacoesDeRelatorio.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.Status == StatusDaSolicitacao.NaFila)
            .OrderBy(s => s.CriadoEm)
            .Take(quantidade)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SolicitacaoDeRelatorio>> ListarVencidasDeTodasAsFormaturas(
        DateTime agora,
        int quantidade,
        CancellationToken ct = default
    ) =>
        await db
            .SolicitacoesDeRelatorio.IgnoreQueryFilters()
            .Where(s => s.ArquivoId != null && s.ExpiraEm != null && s.ExpiraEm <= agora)
            .OrderBy(s => s.ExpiraEm)
            .Take(quantidade)
            .ToListAsync(ct);
}
