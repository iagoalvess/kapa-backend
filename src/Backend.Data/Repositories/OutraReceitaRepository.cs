using Backend.Business.Abstractions;
using Backend.Business.Comunicacao.Models;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Receitas da formatura selecionada (Sprint 28).
/// </summary>
/// <remarks>
/// O espelho do <see cref="DespesaRepository"/>, sem fornecedor: a origem é texto na própria linha.
/// "Atrasada" não é coluna — é prevista com data no passado, resolvida na projeção com o dia de hoje.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class OutraReceitaRepository(AppDbContext db) : IOutraReceitaRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Da mais recente para a mais antiga: a pergunta da tesouraria é "o que entrou por último". O id
    /// desempata, para a paginação não repetir linha entre páginas.
    /// </remarks>
    public async Task<PaginaDe<OutraReceitaResumo>> Listar(
        PaginacaoRequest paginacao,
        FiltroDeOutrasReceitas filtro,
        DateOnly hoje,
        CancellationToken ct = default
    )
    {
        var consulta = Filtrar(filtro, hoje);

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<OutraReceitaResumo>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "receita" => consulta.Por(r => r.Descricao, desc),
            "data" => consulta.Por(r => r.Data, desc),
            "valor" => consulta.Por(r => r.ValorEmCentavos, desc),
            _ => consulta.OrderByDescending(r => r.Data),
        };

        var itens = await Projetar(ordenada.ThenBy(r => r.Id).Skip(paginacao.Pular).Take(paginacao.Tamanho), hoje).ToListAsync(ct);

        return new PaginaDe<OutraReceitaResumo>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContagemDeOutrasReceitas>> Contar(FiltroDeOutrasReceitas filtro, DateOnly hoje, CancellationToken ct = default) =>
        await Filtrar(filtro with { Status = null, Atrasadas = false }, hoje)
            .GroupBy(r => new { r.Status, Atrasada = r.Data < hoje })
            .Select(grupo => new ContagemDeOutrasReceitas(grupo.Key.Status, grupo.Key.Atrasada, grupo.Count(), grupo.Sum(r => r.ValorEmCentavos)))
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<OutraReceitaResumo?> Obter(Guid id, DateOnly hoje, CancellationToken ct = default) =>
        Projetar(db.OutrasReceitas.AsNoTracking().Where(r => r.Id == id), hoje).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<OutraReceita?> ObterParaEdicao(Guid id, CancellationToken ct = default) => db.OutrasReceitas.FirstOrDefaultAsync(r => r.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> ExisteIgual(string descricao, string? origem, DateOnly data, CancellationToken ct = default) =>
        db.OutrasReceitas.AnyAsync(
            r => r.Descricao == descricao && r.Origem == origem && r.Data == data && r.Status != StatusDaOutraReceita.Cancelada,
            ct
        );

    /// <inheritdoc />
    public async Task Adicionar(OutraReceita outraReceita, CancellationToken ct = default) => await db.OutrasReceitas.AddAsync(outraReceita, ct);

    private IQueryable<OutraReceita> Filtrar(FiltroDeOutrasReceitas filtro, DateOnly hoje)
    {
        var consulta = db.OutrasReceitas.AsNoTracking();

        if (filtro.Categoria is { } categoria)
            consulta = consulta.Where(r => r.Categoria == categoria);

        if (filtro.Status is { } status)
            consulta = consulta.Where(r => r.Status == status);

        if (filtro.Atrasadas)
            consulta = consulta.Where(r => r.Status == StatusDaOutraReceita.Prevista && r.Data < hoje);

        if (filtro.De is { } de)
            consulta = consulta.Where(r => r.Data >= de);

        if (filtro.Ate is { } ate)
            consulta = consulta.Where(r => r.Data <= ate);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = Busca.Padrao(filtro.Busca);

            consulta = consulta.Where(r =>
                EF.Functions.ILike(EF.Functions.Unaccent(r.Descricao), termo)
                || (r.Origem != null && EF.Functions.ILike(EF.Functions.Unaccent(r.Origem), termo))
            );
        }

        return consulta;
    }

    /// <summary>A receita com o comprovante — só quando o documento ainda é visível para a turma.</summary>
    private IQueryable<OutraReceitaResumo> Projetar(IQueryable<OutraReceita> consulta, DateOnly hoje) =>
        consulta.Select(r => new OutraReceitaResumo(
            r.Id,
            r.Descricao,
            r.Origem,
            r.Categoria,
            r.ValorEmCentavos,
            r.Data,
            r.Status,
            (
                from documento in db.Documentos
                join arquivo in db.Arquivos on documento.ArquivoId equals arquivo.Id
                where documento.Id == r.DocumentoId && documento.Visibilidade == Visibilidade.Turma
                select new DocumentoDoAcervo(documento.Id, documento.Titulo, arquivo.Nome, arquivo.ContentType)
            ).FirstOrDefault(),
            r.Status == StatusDaOutraReceita.Prevista && r.Data < hoje
        ));
}
