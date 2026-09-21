using Backend.Business.Abstractions;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Informes de pagamento da formatura selecionada.
/// </summary>
/// <remarks>
/// A fila junta cada informe à parcela pela base de <see cref="ParcelaRepository"/>: a parcela aparece
/// igual na lista da gestão, no extrato e na conferência.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class InformeRepository(AppDbContext db) : IInformeRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListarParcelas(IReadOnlyCollection<Guid> informeIds, CancellationToken ct = default) =>
        await db.Informes.AsNoTracking().Where(i => informeIds.Contains(i.Id)).Select(i => i.ParcelaId).Distinct().ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InformeDePagamento>> ListarParaEdicao(IReadOnlyCollection<Guid> informeIds, CancellationToken ct = default) =>
        await db.Informes.Where(i => informeIds.Contains(i.Id)).ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Pendentes do mais antigo para o mais novo — a fila é a do tempo, e o aviso que espera há mais
    /// tempo é o primeiro a olhar (decisão 4). Os já conferidos, dos mais recentes: é o histórico.
    /// Desempate pelo id.
    /// <para>
    /// "Formando" não é coluna de ordenação: o nome vem da parcela, buscada depois por id, e trazê-la
    /// para dentro desta consulta só para ordenar sairia caro.
    /// </para>
    /// </remarks>
    public async Task<PaginaDe<InformeNaFila>> Listar(
        PaginacaoRequest paginacao,
        FiltroDeInformes filtro,
        DateOnly hoje,
        DateTime? conferidosDesdeUtc = null,
        CancellationToken ct = default
    )
    {
        var status = filtro.Status;
        var consulta = db.Informes.AsNoTracking().Where(i => i.Status == status);

        if (conferidosDesdeUtc is { } desde)
            consulta = consulta.Where(i => i.ConferidoEm >= desde);

        if (filtro.De is { } de)
            consulta = consulta.Where(i => i.PagoEm >= de);

        if (filtro.Ate is { } ate)
            consulta = consulta.Where(i => i.PagoEm <= ate);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var doFormando = ParcelaRepository.ParcelasDe(db, filtro.Busca);
            consulta = consulta.Where(i => doFormando.Contains(i.ParcelaId));
        }

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<InformeNaFila>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "pagoEm" => consulta.Por(i => i.PagoEm, desc),
            "recebido" => consulta.Por(i => i.ValorEmCentavos, desc),
            "conferido" => consulta.Por(i => i.ConferidoEm, desc),
            _ when status == StatusDoInforme.Pendente => consulta.OrderBy(i => i.CriadoEm),
            _ => consulta.OrderByDescending(i => i.ConferidoEm),
        };

        var informes = await ordenada.ThenBy(i => i.Id).Skip(paginacao.Pular).Take(paginacao.Tamanho).ToListAsync(ct);
        var ids = informes.Select(i => i.ParcelaId).ToList();
        var parcelas = ParcelaRepository
            .NoDia(await ParcelaRepository.Projetar(ParcelaRepository.Linhas(db).Where(l => ids.Contains(l.Parcela.Id))).ToListAsync(ct), hoje)
            .ToDictionary(p => p.Id);

        return new PaginaDe<InformeNaFila>(
            [
                .. informes.Select(informe => new InformeNaFila(
                    informe.Id,
                    parcelas[informe.ParcelaId],
                    informe.PagoEm,
                    informe.ValorEmCentavos,
                    0,
                    informe.ComprovanteArquivoId is not null,
                    informe.MeioEscolhido,
                    informe.Status,
                    informe.CriadoEm,
                    informe.ConferidoEm
                )),
            ],
            paginacao.Pagina,
            paginacao.Tamanho,
            total
        );
    }

    /// <inheritdoc />
    public Task<ComprovanteDoInforme?> ObterComprovante(Guid informeId, CancellationToken ct = default) =>
        (
            from informe in db.Informes.AsNoTracking()
            join arquivo in db.Arquivos.AsNoTracking() on informe.ComprovanteArquivoId equals (Guid?)arquivo.Id
            where informe.Id == informeId
            select new ComprovanteDoInforme(arquivo.Id, arquivo.EnviadoPorId)
        ).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task Adicionar(InformeDePagamento informe, CancellationToken ct = default) => await db.Informes.AddAsync(informe, ct);
}
