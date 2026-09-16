using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// A conta de recebimento da formatura selecionada.
/// </summary>
/// <remarks>
/// Sem <c>where</c> de formatura: o filtro global já restringe à da sessão, e o índice único em
/// <c>formatura_id</c> garante que há no máximo uma.
/// <para>
/// Por isso <c>SingleOrDefault</c>, e não <c>First</c>: com <c>First</c> sem <c>where</c> nem
/// <c>order by</c>, o EF avisa em log a cada consulta que o resultado é imprevisível — e ele tem
/// razão em geral, mas aqui a unicidade é do índice. <c>Single</c> diz isso no código.
/// </para>
/// </remarks>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class ContaDeRecebimentoRepository(AppDbContext db) : IContaDeRecebimentoRepository
{
    /// <inheritdoc />
    /// <remarks><c>LEFT JOIN</c> com o usuário: a conta não conferida não tem quem conferiu.</remarks>
    public Task<ContaDeRecebimentoDetalhe?> ObterDetalhe(CancellationToken ct = default) =>
        (
            from conta in db.ContasDeRecebimento.AsNoTracking()
            join usuario in db.Users.AsNoTracking() on conta.ConferidaPorUsuarioId equals (Guid?)usuario.Id into conferentes
            from conferente in conferentes.DefaultIfEmpty()
            select new ContaDeRecebimentoDetalhe(
                conta.TipoDeChave,
                conta.Chave,
                conta.NomeDoTitular,
                conta.Cidade,
                conta.AtualizadoEm,
                conta.ConferidaEm,
                conferente == null ? null : conferente.Nome
            )
        ).SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<ContaDeRecebimento?> ObterParaEdicao(CancellationToken ct = default) => db.ContasDeRecebimento.SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task Adicionar(ContaDeRecebimento conta, CancellationToken ct = default) => await db.ContasDeRecebimento.AddAsync(conta, ct);
}
