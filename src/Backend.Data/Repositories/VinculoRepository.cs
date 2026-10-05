using Backend.Business.Abstractions;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Consultas sobre o vínculo entre usuário e formatura.
/// </summary>
/// <remarks>
/// Nenhuma consulta daqui depende de formatura selecionada — e não pode depender:
/// <see cref="VinculoDeFormatura"/> é justamente o que responde "quais turmas você pode
/// escolher?", pergunta feita antes de existir a claim <c>formatura_id</c> no token.
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class VinculoRepository(AppDbContext db) : IVinculoRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// A ordenação vem <b>antes</b> da projeção: ordenar pelo campo do <c>record</c> já
    /// construído não é traduzível para SQL, e o EF desiste da consulta inteira.
    /// <para>
    /// <c>Nome</c> com desempate por <c>Id</c> — sem ele, duas turmas homônimas trocam de lugar
    /// entre chamadas e o seletor pisca.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<FormaturaDoUsuario>> ListarDoUsuario(Guid usuarioId, CancellationToken ct = default) =>
        await (
            from vinculo in db.Vinculos.AsNoTracking()
            join formatura in db.Formaturas on vinculo.FormaturaId equals formatura.Id
            where vinculo.UsuarioId == usuarioId && (vinculo.Ativo || vinculo.DesligadoEm != null)
            orderby formatura.Nome, formatura.Id
            select new FormaturaDoUsuario(
                formatura.Id,
                formatura.Nome,
                formatura.Curso,
                formatura.Instituicao,
                formatura.Ano,
                formatura.Semestre,
                vinculo.Papel,
                vinculo.DesligadoEm
            )
        ).ToListAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// <c>Take(2)</c> porque a pergunta é "quantos, até dois?": basta saber se passou de um.
    /// Um <c>Count()</c> sobre a tabela inteira responderia a mesma coisa lendo tudo.
    /// </remarks>
    public async Task<VinculoAtivo?> ObterUnicoAtivo(Guid usuarioId, CancellationToken ct = default)
    {
        var ativos = await db
            .Vinculos.AsNoTracking()
            .Where(v => v.UsuarioId == usuarioId && (v.Ativo || v.DesligadoEm != null))
            .Select(v => new VinculoAtivo(v.FormaturaId, v.Papel, v.DesligadoEm))
            .Take(2)
            .ToListAsync(ct);

        return ativos.Count == 1 ? ativos[0] : null;
    }

    /// <inheritdoc />
    public Task<string?> ObterPapelAtivo(Guid usuarioId, Guid formaturaId, CancellationToken ct = default) =>
        db
            .Vinculos.AsNoTracking()
            .Where(v => v.UsuarioId == usuarioId && v.FormaturaId == formaturaId && v.Ativo)
            .Select(v => v.Papel)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>Uma consulta: o vínculo ativo de papel Formando, a existência do termo e a da adesão, por <c>EXISTS</c>.</remarks>
    public Task<bool> FormandoSemAdesao(Guid usuarioId, Guid formaturaId, CancellationToken ct = default) =>
        db
            .Vinculos.AsNoTracking()
            .AnyAsync(
                v =>
                    v.UsuarioId == usuarioId
                    && v.FormaturaId == formaturaId
                    && v.Ativo
                    && v.Papel == PapelNaFormatura.Formando
                    && db.TermosDeAdesao.Any(t => t.FormaturaId == formaturaId)
                    && !db.Adesoes.Any(a => a.VinculoId == v.Id),
                ct
            );

    /// <inheritdoc />
    public Task<VinculoAtivo?> ObterDoTitular(Guid usuarioId, Guid formaturaId, CancellationToken ct = default) =>
        db
            .Vinculos.AsNoTracking()
            .Where(v => v.UsuarioId == usuarioId && v.FormaturaId == formaturaId && (v.Ativo || v.DesligadoEm != null))
            .Select(v => new VinculoAtivo(v.FormaturaId, v.Papel, v.DesligadoEm))
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// <c>LEFT JOIN</c> do vínculo com o perfil: quem nunca abriu o cadastro precisa aparecer —
    /// é justamente quem a comissão quer cobrar. Sem perfil, conta como 0% e essencial pendente.
    /// <para>
    /// Ativos primeiro, depois nome, com desempate por <c>Id</c> do usuário — sem critério único,
    /// dois membros homônimos trocam de lugar entre páginas e um some da listagem. Quem está na
    /// turma fica em cima porque removido é histórico, e histórico não disputa atenção.
    /// </para>
    /// <para>
    /// <c>TemAdesao</c> é subconsulta correlacionada, um <c>EXISTS</c> por linha: a adesão vive sob o
    /// filtro global da formatura, que aqui é sempre a mesma que chega em <c>formaturaId</c> (vem da
    /// claim).
    /// </para>
    /// </remarks>
    public async Task<PaginaDe<MembroDaFormatura>> ListarMembros(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeMembros filtro,
        CancellationToken ct = default
    )
    {
        var consulta =
            from vinculo in db.Vinculos.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            join perfil in db.PerfisDeFormandos.AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
            from perfil in perfis.DefaultIfEmpty()
            where vinculo.FormaturaId == formaturaId
            select new
            {
                vinculo,
                usuario,
                NomeCompleto = perfil == null ? null : perfil.NomeCompleto,
                Completude = perfil == null ? 0 : perfil.Completude,
                EssencialPreenchido = perfil != null && perfil.EssencialPreenchido,
                TemAdesao = db.Adesoes.Any(adesao => adesao.VinculoId == vinculo.Id),
            };

        if (filtro.Ativo is { } ativo)
            consulta = consulta.Where(linha => linha.vinculo.Ativo == ativo);

        if (filtro.Desligado is { } desligado)
            consulta = consulta.Where(linha => (linha.vinculo.DesligadoEm != null) == desligado);

        if (filtro.Papel is { } papel)
            consulta = consulta.Where(linha => linha.vinculo.Papel == papel);

        consulta = filtro.Cadastro switch
        {
            SituacaoDoCadastro.Pendente => consulta.Where(linha => !linha.EssencialPreenchido),
            SituacaoDoCadastro.Incompleto => consulta.Where(linha => linha.Completude < 100),
            SituacaoDoCadastro.Completo => consulta.Where(linha => linha.Completude == 100),
            _ => consulta,
        };

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = Busca.Padrao(filtro.Busca);
            consulta = consulta.Where(linha =>
                EF.Functions.ILike(EF.Functions.Unaccent(linha.usuario.Nome), termo)
                || EF.Functions.ILike(linha.usuario.Email!, termo)
                || (linha.NomeCompleto != null && EF.Functions.ILike(EF.Functions.Unaccent(linha.NomeCompleto), termo))
            );
        }

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<MembroDaFormatura>.Vazia(paginacao);

        var desc = paginacao.Descendente;
        var ordenada = paginacao.OrdenarPor switch
        {
            "membro" => consulta.Por(linha => linha.usuario.Nome, desc),
            "papel" => consulta.Por(linha => linha.vinculo.Papel, desc),
            "cadastro" => consulta.Por(linha => linha.Completude, desc),
            "situacao" => consulta.Por(linha => linha.vinculo.Ativo, desc),
            _ => consulta.OrderByDescending(linha => linha.vinculo.Ativo).ThenBy(linha => linha.usuario.Nome),
        };

        var itens = await ordenada
            .ThenBy(linha => linha.usuario.Id)
            .Skip(paginacao.Pular)
            .Take(paginacao.Tamanho)
            .Select(linha => new MembroDaFormatura(
                linha.usuario.Id,
                linha.usuario.Nome,
                linha.usuario.Email ?? string.Empty,
                linha.vinculo.Papel,
                linha.vinculo.Ativo,
                linha.NomeCompleto,
                linha.Completude,
                !linha.EssencialPreenchido,
                linha.TemAdesao,
                linha.vinculo.DesligadoEm,
                linha.vinculo.MotivoDoDesligamento,
                linha.vinculo.DetalheDoDesligamento
            ))
            .ToListAsync(ct);

        return new PaginaDe<MembroDaFormatura>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    public Task TravarEntradas(Guid formaturaId, CancellationToken ct = default) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({formaturaId.ToString()}, 0))", ct);

    /// <inheritdoc />
    /// <remarks>
    /// <c>LEFT JOIN</c> do perfil, como em <see cref="ListarMembros"/>: quem nunca abriu o cadastro é
    /// justamente quem falta, e um <c>INNER JOIN</c> o deixaria de fora da contagem.
    /// </remarks>
    public async Task<IReadOnlyList<ContagemDeMembros>> ContarMembros(Guid formaturaId, CancellationToken ct = default) =>
        await (
            from vinculo in db.Vinculos.AsNoTracking()
            join perfil in db.PerfisDeFormandos.AsNoTracking() on vinculo.Id equals perfil.VinculoId into perfis
            from perfil in perfis.DefaultIfEmpty()
            where vinculo.FormaturaId == formaturaId
            select new
            {
                vinculo.Papel,
                vinculo.Ativo,
                Desligado = vinculo.DesligadoEm != null,
                EssencialPendente = perfil == null || !perfil.EssencialPreenchido,
            }
        )
            .GroupBy(linha => new
            {
                linha.Papel,
                linha.Ativo,
                linha.Desligado,
                linha.EssencialPendente,
            })
            .Select(grupo => new ContagemDeMembros(grupo.Key.Papel, grupo.Key.Ativo, grupo.Key.Desligado, grupo.Key.EssencialPendente, grupo.Count()))
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task<VinculoDeFormatura?> ObterAtivoParaEdicao(Guid usuarioId, Guid formaturaId, CancellationToken ct = default) =>
        db.Vinculos.FirstOrDefaultAsync(v => v.UsuarioId == usuarioId && v.FormaturaId == formaturaId && v.Ativo, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<VinculoDeFormatura>> ListarAtivosParaEdicao(Guid formaturaId, CancellationToken ct = default) =>
        await db.Vinculos.Where(v => v.FormaturaId == formaturaId && v.Ativo).ToListAsync(ct);

    /// <inheritdoc />
    public Task<VinculoDeFormatura?> ObterParaEdicao(Guid usuarioId, Guid formaturaId, CancellationToken ct = default) =>
        db.Vinculos.FirstOrDefaultAsync(v => v.UsuarioId == usuarioId && v.FormaturaId == formaturaId, ct);

    /// <inheritdoc />
    /// <remarks>
    /// SQL escrito à mão porque o EF Core não expressa <c>FOR UPDATE</c>. Em <c>READ COMMITTED</c>,
    /// quem espera a trava reavalia o <c>WHERE</c> depois do commit de quem a tinha: o presidente
    /// que acabou de ser rebaixado já não entra na contagem.
    /// </remarks>
    public async Task<int> TravarPresidentesAtivos(Guid formaturaId, CancellationToken ct = default) =>
        (
            await db
                .Vinculos.FromSql(
                    $"""
                    SELECT * FROM vinculos_de_formatura
                    WHERE formatura_id = {formaturaId} AND ativo AND papel = {PapelNaFormatura.Presidente}
                    FOR UPDATE
                    """
                )
                .AsNoTracking()
                .ToListAsync(ct)
        ).Count;

    /// <inheritdoc />
    public async Task Adicionar(VinculoDeFormatura vinculo, CancellationToken ct = default) => await db.Vinculos.AddAsync(vinculo, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListarEmailsDosPresidentes(Guid formaturaId, CancellationToken ct = default) =>
        await (
            from vinculo in db.Vinculos.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            where vinculo.FormaturaId == formaturaId && vinculo.Ativo && vinculo.Papel == PapelNaFormatura.Presidente && usuario.Email != null
            select usuario.Email!
        ).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListarEmailsDaComissao(Guid formaturaId, CancellationToken ct = default) =>
        await (
            from vinculo in db.Vinculos.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            where vinculo.FormaturaId == formaturaId && vinculo.Ativo && vinculo.Papel != PapelNaFormatura.Formando && usuario.Email != null
            select usuario.Email!
        ).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListarEmailsDosFormandos(Guid formaturaId, CancellationToken ct = default) =>
        await (
            from vinculo in db.Vinculos.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            where vinculo.FormaturaId == formaturaId && vinculo.Ativo && vinculo.Papel == PapelNaFormatura.Formando && usuario.Email != null
            select usuario.Email!
        ).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListarEmailsDaTesouraria(Guid formaturaId, CancellationToken ct = default) =>
        await (
            from vinculo in db.Vinculos.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            where vinculo.FormaturaId == formaturaId && vinculo.Ativo && PapelNaFormatura.Tesouraria.Contains(vinculo.Papel) && usuario.Email != null
            select usuario.Email!
        ).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> ListarEmailsDosVinculos(
        IReadOnlyCollection<Guid> vinculoIds,
        CancellationToken ct = default
    ) =>
        await (
            from vinculo in db.Vinculos.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on vinculo.UsuarioId equals usuario.Id
            where vinculoIds.Contains(vinculo.Id) && usuario.Email != null
            select new { vinculo.Id, Email = usuario.Email! }
        ).ToDictionaryAsync(linha => linha.Id, linha => linha.Email, ct);
}
