using Backend.Business.Abstractions;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Recebimentos — as entradas no caixa — da formatura selecionada.
/// </summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class RecebimentoRepository(AppDbContext db) : IRecebimentoRepository
{
    /// <inheritdoc />
    public Task<Recebimento?> ObterAtivoParaEdicao(Guid parcelaId, CancellationToken ct = default) =>
        db.Recebimentos.FirstOrDefaultAsync(r => r.ParcelaId == parcelaId && r.EstornadoEm == null, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Das baixas mais recentes; o nome de quem baixou é o da conta — é a comissão, não o formando.
    /// <para>
    /// "Formando" e "Diferença" não são colunas de ordenação: o nome vem da parcela, buscada depois
    /// por id, e a diferença é a subtração de duas colunas, feita na projeção.
    /// </para>
    /// </remarks>
    public async Task<PaginaDe<Divergencia>> ListarDivergencias(
        PaginacaoRequest paginacao,
        DateOnly hoje,
        string? busca = null,
        CancellationToken ct = default
    )
    {
        var consulta =
            from recebimento in db.Recebimentos.AsNoTracking()
            join autor in db.Users.AsNoTracking() on recebimento.BaixadoPorUsuarioId equals autor.Id
            where recebimento.EstornadoEm == null && recebimento.ValorEmCentavos != recebimento.DevidoEmCentavos
            select new { recebimento, BaixadoPor = autor.Nome };

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var doFormando = ParcelaRepository.ParcelasDe(db, busca);
            consulta = consulta.Where(linha => doFormando.Contains(linha.recebimento.ParcelaId));
        }

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<Divergencia>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "pagoEm" => consulta.Por(x => x.recebimento.PagoEm, desc),
            "devido" => consulta.Por(x => x.recebimento.DevidoEmCentavos, desc),
            "recebido" => consulta.Por(x => x.recebimento.ValorEmCentavos, desc),
            "baixa" => consulta.Por(x => x.recebimento.BaixadoEm, desc),
            _ => consulta.OrderByDescending(x => x.recebimento.BaixadoEm),
        };

        var linhas = await ordenada.ThenBy(x => x.recebimento.Id).Skip(paginacao.Pular).Take(paginacao.Tamanho).ToListAsync(ct);
        var ids = linhas.Select(x => x.recebimento.ParcelaId).ToList();
        var parcelas = ParcelaRepository
            .NoDia(await ParcelaRepository.Projetar(ParcelaRepository.Linhas(db).Where(l => ids.Contains(l.Parcela.Id))).ToListAsync(ct), hoje)
            .ToDictionary(p => p.Id);

        return new PaginaDe<Divergencia>(
            [
                .. linhas.Select(x => new Divergencia(
                    x.recebimento.Id,
                    parcelas[x.recebimento.ParcelaId],
                    x.recebimento.PagoEm,
                    x.recebimento.DevidoEmCentavos,
                    x.recebimento.ValorEmCentavos,
                    x.recebimento.Forma,
                    x.BaixadoPor
                )),
            ],
            paginacao.Pagina,
            paginacao.Tamanho,
            total
        );
    }

    /// <inheritdoc />
    public async Task Adicionar(Recebimento recebimento, CancellationToken ct = default) => await db.Recebimentos.AddAsync(recebimento, ct);
}
