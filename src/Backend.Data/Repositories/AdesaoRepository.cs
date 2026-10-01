using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Business.Formaturas.Models;
using Backend.Data.Context;
using Backend.Data.Criptografia;
using Backend.Data.Mappings;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Termos e adesões da formatura selecionada.
/// </summary>
/// <remarks>
/// Termo e adesão são isolados pelo filtro global; o vínculo não — por isso o painel, que parte do
/// vínculo, leva a formatura explícita, como em <see cref="VinculoRepository"/>.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
/// <param name="cifra">HMAC do CPF, a coluna pesquisável ao lado da cifrada.</param>
public sealed class AdesaoRepository(AppDbContext db, CifraDeCampo cifra) : IAdesaoRepository
{
    /// <inheritdoc />
    public Task<VersaoDoTermo?> ObterTermoVigente(CancellationToken ct = default) =>
        db
            .TermosDeAdesao.AsNoTracking()
            .OrderByDescending(t => t.Versao)
            .Select(t => new VersaoDoTermo(t.Id, t.Versao, t.Conteudo, t.VigenteDesde))
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>Poucas linhas por turma — uma por publicação —, por isso sem paginação.</remarks>
    public async Task<IReadOnlyList<TermoPublicado>> ListarTermos(CancellationToken ct = default) =>
        await db
            .TermosDeAdesao.AsNoTracking()
            .OrderByDescending(t => t.Versao)
            .Select(t => new TermoPublicado(t.Id, t.Versao, t.VigenteDesde, db.Adesoes.Count(a => a.TermoId == t.Id)))
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task AdicionarTermo(TermoDaFormatura termo, CancellationToken ct = default) => await db.TermosDeAdesao.AddAsync(termo, ct);

    /// <inheritdoc />
    public Task<VersaoDoTermo?> ObterTermo(Guid termoId, CancellationToken ct = default) =>
        db
            .TermosDeAdesao.AsNoTracking()
            .Where(t => t.Id == termoId)
            .Select(t => new VersaoDoTermo(t.Id, t.Versao, t.Conteudo, t.VigenteDesde))
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<string?> ObterResumo(Guid termoId, CancellationToken ct = default) =>
        db.ResumosDeTermo.AsNoTracking().Where(r => r.TermoId == termoId).Select(r => (string?)r.Texto).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Os dois <c>IgnoreQueryFilters</c> são o ponto do método: o job ainda não apontou escopo para
    /// turma nenhuma, e o filtro global não casaria com linha alguma.
    /// </remarks>
    public async Task<IReadOnlyList<TermoSemResumo>> ListarTermosSemResumoDeTodasAsFormaturas(
        DateTime publicadosDesde,
        int limite,
        CancellationToken ct = default
    ) =>
        await (
            from termo in db.TermosDeAdesao.AsNoTracking().IgnoreQueryFilters()
            join formatura in db.Formaturas.AsNoTracking() on termo.FormaturaId equals formatura.Id
            where
                formatura.Status == StatusDaFormatura.Ativa
                && termo.VigenteDesde >= publicadosDesde
                && !db.ResumosDeTermo.Any(r => r.TermoId == termo.Id)
                && !db.TermosDeAdesao.IgnoreQueryFilters().Any(outro => outro.FormaturaId == termo.FormaturaId && outro.Versao > termo.Versao)
            orderby termo.VigenteDesde
            select new TermoSemResumo(termo.FormaturaId, termo.Id)
        )
            .Take(limite)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task AdicionarResumo(ResumoDoTermo resumo, CancellationToken ct = default) => await db.ResumosDeTermo.AddAsync(resumo, ct);

    /// <inheritdoc />
    public Task<bool> JaAderiu(Guid vinculoId, Guid termoId, CancellationToken ct = default) =>
        db.Adesoes.AnyAsync(a => a.VinculoId == vinculoId && a.TermoId == termoId, ct);

    /// <inheritdoc />
    public Task<bool> JaAderiuAlgumaVez(Guid vinculoId, CancellationToken ct = default) => db.Adesoes.AnyAsync(a => a.VinculoId == vinculoId, ct);

    /// <inheritdoc />
    public Task<bool> CpfEmUsoPorOutro(string cpf, Guid vinculoId, CancellationToken ct = default)
    {
        var hmac = cifra.Hmac(cpf);

        return db.Adesoes.AnyAsync(a => EF.Property<string>(a, AdesaoDoFormandoMapping.PropriedadeDoHmac) == hmac && a.VinculoId != vinculoId, ct);
    }

    /// <inheritdoc />
    public async Task Adicionar(AdesaoDoFormando adesao, CancellationToken ct = default)
    {
        var entrada = await db.Adesoes.AddAsync(adesao, ct);

        entrada.Property(AdesaoDoFormandoMapping.PropriedadeDoHmac).CurrentValue = cifra.Hmac(adesao.Cpf);
    }

    /// <inheritdoc />
    public Task<AdesaoComTermo?> ObterUltimaDoVinculo(Guid vinculoId, CancellationToken ct = default) =>
        ComTermo(db.Adesoes.Where(a => a.VinculoId == vinculoId)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<AdesaoComTermo?> Obter(Guid adesaoId, CancellationToken ct = default) =>
        ComTermo(db.Adesoes.Where(a => a.Id == adesaoId)).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// A adesão de cada linha é a mais recente do vínculo, por subconsulta correlacionada — uma por
    /// coluna, que o EF traduz sem trazer a tabela de adesões para a memória. Nome civil do cadastro
    /// quando houver, senão o da conta; ordem por nome com desempate pelo id do usuário.
    /// <para>
    /// Ordenar por "situação" é ordenar por <c>AdesaoId != null</c>: quem aderiu tem adesão, e é a
    /// mesma coluna que a pílula "Aderiram" filtra.
    /// </para>
    /// </remarks>
    public async Task<PaginaDe<SituacaoDeAdesao>> ListarSituacoes(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeAdesoes filtro,
        CancellationToken ct = default
    )
    {
        var consulta =
            from vinculo in db.Vinculos.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            join perfil in db.PerfisDeFormandos.AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
            from perfil in perfis.DefaultIfEmpty()
            where vinculo.FormaturaId == formaturaId && vinculo.Ativo
            select new
            {
                UsuarioId = usuario.Id,
                Nome = perfil != null && perfil.NomeCompleto != null ? perfil.NomeCompleto : usuario.Nome,
                Email = usuario.Email ?? string.Empty,
                vinculo.Papel,
                AdesaoId = db
                    .Adesoes.Where(a => a.VinculoId == vinculo.Id)
                    .OrderByDescending(a => a.Versao)
                    .Select(a => (Guid?)a.Id)
                    .FirstOrDefault(),
                Versao = db.Adesoes.Where(a => a.VinculoId == vinculo.Id).Max(a => (int?)a.Versao),
                AceitoEm = db
                    .Adesoes.Where(a => a.VinculoId == vinculo.Id)
                    .OrderByDescending(a => a.Versao)
                    .Select(a => (DateTime?)a.AceitoEm)
                    .FirstOrDefault(),
            };

        if (filtro.Aderiu is { } aderiu)
            consulta = aderiu ? consulta.Where(linha => linha.AdesaoId != null) : consulta.Where(linha => linha.AdesaoId == null);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = Busca.Padrao(filtro.Busca);
            consulta = consulta.Where(linha =>
                EF.Functions.ILike(EF.Functions.Unaccent(linha.Nome), termo) || EF.Functions.ILike(linha.Email, termo)
            );
        }

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<SituacaoDeAdesao>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "membro" => consulta.Por(linha => linha.Nome, desc),
            "papel" => consulta.Por(linha => linha.Papel, desc),
            "situacao" => consulta.Por(linha => linha.AdesaoId != null, desc),
            "aceito_em" => consulta.Por(linha => linha.AceitoEm, desc),
            _ => consulta.OrderBy(linha => linha.Nome),
        };

        var itens = await ordenada
            .ThenBy(linha => linha.UsuarioId)
            .Skip(paginacao.Pular)
            .Take(paginacao.Tamanho)
            .Select(linha => new SituacaoDeAdesao(
                linha.UsuarioId,
                linha.Nome,
                linha.Email,
                linha.Papel,
                linha.AdesaoId,
                linha.Versao,
                linha.AceitoEm
            ))
            .ToListAsync(ct);

        return new PaginaDe<SituacaoDeAdesao>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    public async Task<(int Membros, int Aderiram)> Contar(Guid formaturaId, CancellationToken ct = default)
    {
        var membros = db.Vinculos.AsNoTracking().Where(v => v.FormaturaId == formaturaId && v.Ativo);

        return (await membros.CountAsync(ct), await membros.CountAsync(v => db.Adesoes.Any(a => a.VinculoId == v.Id), ct));
    }

    /// <summary>A adesão mais recente do recorte, junto do texto da versão que ela aceitou.</summary>
    private IQueryable<AdesaoComTermo> ComTermo(IQueryable<AdesaoDoFormando> adesoes) =>
        from adesao in adesoes.AsNoTracking()
        join termo in db.TermosDeAdesao.AsNoTracking() on adesao.TermoId equals termo.Id
        orderby adesao.Versao descending, adesao.AceitoEm descending
        select new AdesaoComTermo(adesao, termo.Conteudo);
}
