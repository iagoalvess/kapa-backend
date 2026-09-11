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
            select new FormaturaDoUsuario(formatura.Id, formatura.Nome, vinculo.Papel)
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
    public async Task Adicionar(VinculoDeFormatura vinculo, CancellationToken ct = default) => await db.Vinculos.AddAsync(vinculo, ct);
}
