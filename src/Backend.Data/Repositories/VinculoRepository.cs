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
            where vinculo.UsuarioId == usuarioId && vinculo.Ativo
            orderby formatura.Nome, formatura.Id
            select new FormaturaDoUsuario(
                formatura.Id,
                formatura.Nome,
                formatura.Curso,
                formatura.Instituicao,
                formatura.Ano,
                formatura.Semestre,
                vinculo.Papel
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
            .Where(v => v.UsuarioId == usuarioId && v.Ativo)
            .Select(v => new VinculoAtivo(v.FormaturaId, v.Papel))
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
    /// <remarks>
    /// Ativos primeiro, depois nome, com desempate por <c>Id</c> do usuário — sem critério único,
    /// dois membros homônimos trocam de lugar entre páginas e um some da listagem.
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
            where vinculo.FormaturaId == formaturaId
            select new { vinculo, usuario };

        if (filtro.Ativo is { } ativo)
            consulta = consulta.Where(linha => linha.vinculo.Ativo == ativo);

        if (filtro.Papel is { } papel)
            consulta = consulta.Where(linha => linha.vinculo.Papel == papel);

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var termo = $"%{filtro.Busca.Trim()}%";
            consulta = consulta.Where(linha => EF.Functions.ILike(linha.usuario.Nome, termo) || EF.Functions.ILike(linha.usuario.Email!, termo));
        }

        var total = await consulta.LongCountAsync(ct);

        if (total == 0)
            return PaginaDe<MembroDaFormatura>.Vazia(paginacao);

        var itens = await consulta
            .OrderByDescending(linha => linha.vinculo.Ativo)
            .ThenBy(linha => linha.usuario.Nome)
            .ThenBy(linha => linha.usuario.Id)
            .Skip(paginacao.Pular)
            .Take(paginacao.Tamanho)
            .Select(linha => new MembroDaFormatura(
                linha.usuario.Id,
                linha.usuario.Nome,
                linha.usuario.Email ?? string.Empty,
                linha.vinculo.Papel,
                linha.vinculo.Ativo
            ))
            .ToListAsync(ct);

        return new PaginaDe<MembroDaFormatura>(itens, paginacao.Pagina, paginacao.Tamanho, total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContagemDeMembros>> ContarMembros(Guid formaturaId, CancellationToken ct = default) =>
        await db
            .Vinculos.AsNoTracking()
            .Where(v => v.FormaturaId == formaturaId)
            .GroupBy(v => new { v.Papel, v.Ativo })
            .Select(grupo => new ContagemDeMembros(grupo.Key.Papel, grupo.Key.Ativo, grupo.Count()))
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
}
