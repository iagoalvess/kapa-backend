using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Comunicacao.Interfaces;
using Backend.Business.Comunicacao.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// O acervo da formatura selecionada, recortado pelo papel de quem consulta.
/// </summary>
/// <remarks>
/// Como no mural, toda leitura parte de <see cref="Linhas"/>, que já aplica a visibilidade — inclusive
/// a que entrega o arquivo para o download. O arquivo vem por <c>JOIN</c>: a tabela de arquivos não é
/// isolada por formatura, mas só se chega a ela pelo documento, que é.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class DocumentoRepository(AppDbContext db) : IDocumentoRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Por categoria e título — a tela agrupa por categoria —, ou pelo envio mais recente com
    /// <c>ordenarPor=enviadoEm</c>, que é o "recentes" do mural. O id desempata.
    /// </remarks>
    public async Task<PaginaDe<DocumentoResumo>> Listar(
        PaginacaoRequest paginacao,
        FiltroDeDocumentos filtro,
        string? papel,
        CancellationToken ct = default
    )
    {
        var consulta = Filtrar(Linhas(papel), filtro);

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<DocumentoResumo>.Vazia(paginacao);

        var ordenada = paginacao.OrdenarPor switch
        {
            "enviadoEm" => consulta.Por(linha => linha.Arquivo.CriadoEm, paginacao.Descendente),
            "titulo" => consulta.Por(linha => linha.Documento.Titulo, paginacao.Descendente),
            _ => consulta.OrderBy(linha => linha.Documento.Categoria).ThenBy(linha => linha.Documento.Titulo),
        };

        var itens = await Projetar(ordenada.ThenBy(linha => linha.Documento.Id).Skip(paginacao.Pular).Take(paginacao.Tamanho)).ToListAsync(ct);

        return new PaginaDe<DocumentoResumo>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    /// <remarks>Uma consulta agrupada por categoria; o total e o último envio saem da soma dos grupos.</remarks>
    public async Task<ResumoDoAcervo> Resumir(string? papel, CancellationToken ct = default)
    {
        var grupos = await Linhas(papel)
            .GroupBy(linha => linha.Documento.Categoria)
            .Select(grupo => new
            {
                Categoria = grupo.Key,
                Quantidade = grupo.Count(),
                Bytes = grupo.Sum(linha => linha.Arquivo.Tamanho),
                Ultimo = grupo.Max(linha => linha.Arquivo.CriadoEm),
            })
            .ToListAsync(ct);

        return new ResumoDoAcervo(
            grupos.Sum(g => g.Quantidade),
            grupos.Sum(g => g.Bytes),
            grupos.Count == 0 ? null : grupos.Max(g => g.Ultimo),
            [.. grupos.OrderBy(g => g.Categoria).Select(g => new DocumentosNaCategoria(g.Categoria, g.Quantidade))]
        );
    }

    /// <inheritdoc />
    public Task<DocumentoResumo?> Obter(Guid id, string? papel, CancellationToken ct = default) =>
        Projetar(Linhas(papel).Where(linha => linha.Documento.Id == id)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<ArquivoDoDocumento?> ObterArquivo(Guid id, string? papel, CancellationToken ct = default) =>
        Linhas(papel)
            .Where(linha => linha.Documento.Id == id)
            .Select(linha => new ArquivoDoDocumento(linha.Arquivo.Id, linha.Arquivo.EnviadoPorId, linha.Arquivo.Nome))
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<Documento?> ObterParaEdicao(Guid id, CancellationToken ct = default) => db.Documentos.FirstOrDefaultAsync(d => d.Id == id, ct);

    /// <inheritdoc />
    public async Task Adicionar(Documento documento, CancellationToken ct = default) => await db.Documentos.AddAsync(documento, ct);

    /// <inheritdoc />
    public void Remover(Documento documento) => db.Documentos.Remove(documento);

    /// <summary>
    /// Os documentos que o papel pode ver, cada um com o arquivo atual.
    /// </summary>
    /// <remarks>A gestão vê todos; o resto, só os da turma.</remarks>
    /// <param name="papel">Papel ativo de quem consulta.</param>
    private IQueryable<LinhaDoAcervo> Linhas(string? papel)
    {
        var documentos = db.Documentos.AsNoTracking();

        if (!Visibilidades.VeInternos(papel))
            documentos = documentos.Where(d => d.Visibilidade == Visibilidade.Turma);

        return from documento in documentos
            join arquivo in db.Arquivos.AsNoTracking() on documento.ArquivoId equals arquivo.Id
            select new LinhaDoAcervo { Documento = documento, Arquivo = arquivo };
    }

    private static IQueryable<LinhaDoAcervo> Filtrar(IQueryable<LinhaDoAcervo> consulta, FiltroDeDocumentos filtro)
    {
        if (filtro.Categoria is { } categoria)
            consulta = consulta.Where(linha => linha.Documento.Categoria == categoria);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = Busca.Padrao(filtro.Busca);

            consulta = consulta.Where(linha => EF.Functions.ILike(EF.Functions.Unaccent(linha.Documento.Titulo), termo));
        }

        return consulta;
    }

    /// <summary>A linha com quem enviou o arquivo — <c>LEFT JOIN</c>, para o documento não sumir com a conta.</summary>
    private IQueryable<DocumentoResumo> Projetar(IQueryable<LinhaDoAcervo> consulta) =>
        from linha in consulta
        join usuario in db.Users.AsNoTracking() on linha.Arquivo.EnviadoPorId equals usuario.Id into remetentes
        from remetente in remetentes.DefaultIfEmpty()
        select new DocumentoResumo(
            linha.Documento.Id,
            linha.Documento.Titulo,
            linha.Documento.Categoria,
            linha.Documento.Visibilidade,
            linha.Documento.Versao,
            linha.Arquivo.Nome,
            linha.Arquivo.ContentType,
            linha.Arquivo.Tamanho,
            linha.Arquivo.CriadoEm,
            remetente == null ? null : remetente.Nome
        );

    /// <summary>Um documento com o arquivo atual — a linha em que filtro e ordenação trabalham antes da projeção.</summary>
    private sealed class LinhaDoAcervo
    {
        /// <summary>O documento.</summary>
        public required Documento Documento { get; init; }

        /// <summary>O arquivo atual dele.</summary>
        public required Arquivo Arquivo { get; init; }
    }
}
