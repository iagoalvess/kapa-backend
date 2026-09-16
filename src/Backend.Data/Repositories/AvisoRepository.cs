using Backend.Business.Abstractions;
using Backend.Business.Common.Datas;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Comunicacao.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Avisos da formatura selecionada, recortados pelo papel de quem consulta.
/// </summary>
/// <remarks>
/// Toda leitura parte de <see cref="VisiveisPara"/>: não existe consulta de leitura que ignore a
/// visibilidade, e é por isso que o formando não recebe o aviso interno nem sabendo o id.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class AvisoRepository(AppDbContext db) : IAvisoRepository
{
    /// <inheritdoc />
    /// <remarks>Fixados primeiro, depois do mais novo para o mais antigo, com o id de desempate.</remarks>
    public async Task<PaginaDe<AvisoResumo>> Listar(PaginacaoRequest paginacao, FiltroDeAvisos filtro, string? papel, CancellationToken ct = default)
    {
        var consulta = Filtrar(VisiveisPara(papel), filtro);

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<AvisoResumo>.Vazia(paginacao);

        var ordenada = consulta.OrderByDescending(a => a.Fixado).ThenByDescending(a => a.PublicadoEm).ThenByDescending(a => a.Id);

        var itens = await Projetar(ordenada.Skip(paginacao.Pular).Take(paginacao.Tamanho)).ToListAsync(ct);

        return new PaginaDe<AvisoResumo>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uma consulta só, agrupada por uma constante: são quatro contagens e um máximo sobre as mesmas
    /// linhas, e o mural não tem <c>LEFT JOIN</c> na origem — o que faz o <c>GROUP BY</c> do EF Core
    /// não traduzir. Mural vazio não devolve grupo nenhum, daí o resumo zerado.
    /// </remarks>
    public async Task<ResumoDoMural> Resumir(string? papel, CancellationToken ct = default) =>
        await VisiveisPara(papel)
            .GroupBy(a => 1)
            .Select(grupo => new ResumoDoMural(
                grupo.Count(),
                grupo.Count(a => a.Fixado),
                grupo.Count(a => a.Destaque),
                grupo.Count(a => a.Visibilidade == Visibilidade.SomenteComissao),
                grupo.Max(a => (DateTime?)a.PublicadoEm)
            ))
            .SingleOrDefaultAsync(ct)
        ?? new ResumoDoMural(0, 0, 0, 0, null);

    /// <inheritdoc />
    public Task<AvisoResumo?> Obter(Guid id, string? papel, CancellationToken ct = default) =>
        Projetar(VisiveisPara(papel).Where(a => a.Id == id)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<Aviso?> ObterParaEdicao(Guid id, CancellationToken ct = default) => db.Avisos.FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <inheritdoc />
    public Task<int> ContarFixados(CancellationToken ct = default) => db.Avisos.CountAsync(a => a.Fixado, ct);

    /// <inheritdoc />
    public async Task Adicionar(Aviso aviso, CancellationToken ct = default) => await db.Avisos.AddAsync(aviso, ct);

    /// <inheritdoc />
    public void Remover(Aviso aviso) => db.Avisos.Remove(aviso);

    /// <summary>Os avisos que o papel pode ler: a gestão lê todos; o resto, só os da turma.</summary>
    /// <param name="papel">Papel ativo de quem consulta.</param>
    private IQueryable<Aviso> VisiveisPara(string? papel)
    {
        var avisos = db.Avisos.AsNoTracking();

        return Visibilidades.VeInternos(papel) ? avisos : avisos.Where(a => a.Visibilidade == Visibilidade.Turma);
    }

    /// <summary>Os filtros da tela sobre o que o papel já pode ver.</summary>
    /// <remarks>
    /// A busca varre título e texto: o aviso se procura pelo que ele dizia, e não só pelo título que
    /// alguém deu a ele. Sem acento dos dois lados, como toda busca do produto (ver <see cref="Busca"/>).
    /// <para>
    /// O período vem do calendário de quem lê (Brasília) e a coluna é UTC: o limite de cima é o
    /// primeiro instante do dia seguinte, exclusivo, senão o aviso das 22h sumiria do próprio dia.
    /// </para>
    /// </remarks>
    private static IQueryable<Aviso> Filtrar(IQueryable<Aviso> consulta, FiltroDeAvisos filtro)
    {
        if (filtro.Fixado is { } fixado)
            consulta = consulta.Where(a => a.Fixado == fixado);

        if (filtro.Destaque is { } destaque)
            consulta = consulta.Where(a => a.Destaque == destaque);

        if (filtro.Visibilidade is { } visibilidade)
            consulta = consulta.Where(a => a.Visibilidade == visibilidade);

        if (filtro.De is { } de)
            consulta = consulta.Where(a => a.PublicadoEm >= DataUtils.InicioDoDiaEmUtc(de));

        if (filtro.Ate is { } ate)
            consulta = consulta.Where(a => a.PublicadoEm < DataUtils.FimDoDiaEmUtc(ate));

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = Busca.Padrao(filtro.Busca);

            consulta = consulta.Where(a =>
                EF.Functions.ILike(EF.Functions.Unaccent(a.Titulo), termo) || EF.Functions.ILike(EF.Functions.Unaccent(a.Conteudo), termo)
            );
        }

        return consulta;
    }

    /// <summary>O aviso com o nome de quem publicou — <c>LEFT JOIN</c>, para o aviso não sumir com a conta.</summary>
    private IQueryable<AvisoResumo> Projetar(IQueryable<Aviso> consulta) =>
        from aviso in consulta
        join usuario in db.Users.AsNoTracking() on aviso.PublicadoPorUsuarioId equals usuario.Id into autores
        from autor in autores.DefaultIfEmpty()
        select new AvisoResumo(
            aviso.Id,
            aviso.Titulo,
            aviso.Conteudo,
            aviso.Visibilidade,
            aviso.Fixado,
            aviso.Destaque,
            aviso.PublicadoEm,
            aviso.AtualizadoEm,
            aviso.PublicadoPorUsuarioId,
            autor == null ? null : autor.Nome
        );
}
