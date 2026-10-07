using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Backend.Business.Recebimentos.Services;
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
                new MeiosDaConta(
                    conta.TipoDeChave == null || conta.Chave == null
                        ? null
                        : new ChavePixDaConta(
                            conta.TipoDeChave.Value,
                            conta.Chave,
                            conta.NomeDoTitular ?? "",
                            conta.Cidade ?? "",
                            conta.BancoDaChave
                        ),
                    conta.Banco == null
                        ? null
                        : new DadosBancarios(
                            conta.Banco,
                            conta.Agencia ?? "",
                            conta.Conta ?? "",
                            conta.TipoDeConta ?? "",
                            conta.TitularDaConta ?? ""
                        ),
                    conta.DinheiroCom == null ? null : new DinheiroComAlguem(conta.DinheiroCom, conta.DinheiroOnde)
                ),
                conta.AtualizadoEm,
                conta.ConferidaEm,
                conferente == null ? null : conferente.Nome
            )
        ).SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// A consulta vai pela coluna <c>formatura_id</c> e pelo nome, que têm índice; o <c>jsonb</c> só é
    /// lido na linha escolhida, em C# — o Npgsql 10 não traduz extração de caminho (Sprint 14).
    /// </remarks>
    public async Task<MeiosDaConta?> ObterMeiosVigentesEm(Guid formaturaId, DateTime instanteUtc, CancellationToken ct = default)
    {
        var dados = await db
            .Eventos.AsNoTracking()
            .Where(evento =>
                evento.FormaturaId == formaturaId
                && (evento.Nome == ContaDeRecebimentoService.EventoDeCadastro || evento.Nome == ContaDeRecebimentoService.EventoDeTroca)
                && evento.OcorridoEm <= instanteUtc
            )
            .OrderByDescending(evento => evento.OcorridoEm)
            .ThenByDescending(evento => evento.Id)
            .Select(evento => evento.Dados)
            .FirstOrDefaultAsync(ct);

        return dados is null ? null : ContaDeRecebimentoService.MeiosGravados(dados);
    }

    /// <inheritdoc />
    public Task<ContaDeRecebimento?> ObterParaEdicao(CancellationToken ct = default) => db.ContasDeRecebimento.SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task Adicionar(ContaDeRecebimento conta, CancellationToken ct = default) => await db.ContasDeRecebimento.AddAsync(conta, ct);
}
