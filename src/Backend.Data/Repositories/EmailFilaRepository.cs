using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// Acesso à fila de e-mails.
/// </summary>
/// <param name="db">Contexto de dados da requisição.</param>
public sealed class EmailFilaRepository(AppDbContext db) : IEmailFilaRepository
{
    /// <inheritdoc />
    public async Task Adicionar(EmailNaFila email, CancellationToken ct = default) => await db.EmailsFila.AddAsync(email, ct);

    /// <inheritdoc />
    /// <remarks>
    /// <c>FOR UPDATE SKIP LOCKED</c> é o padrão de fila do PostgreSQL: cada worker tranca as
    /// linhas que pegou e **pula** as que outro já trancou, em vez de esperar por elas. Sem isso,
    /// duas réplicas do worker selecionariam o mesmo lote e o destinatário receberia o e-mail
    /// duas vezes.
    /// <para>
    /// As linhas voltam rastreadas pelo contexto, então <c>MarcarEmEnvio</c> é persistido pelo
    /// <c>SalvarAsync</c> da transação que envolve esta chamada. O envio em si acontece **depois**
    /// do commit — manter a transação aberta durante o SMTP seguraria os locks pelo tempo da rede.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<EmailNaFila>> ReservarLote(int tamanho, DateTime agoraUtc, CancellationToken ct = default)
    {
        var pendente = (int)EEmailStatus.Pendente;

        var lote = await db
            .EmailsFila.FromSql(
                $"""
                SELECT *
                  FROM emails_fila
                 WHERE status = {pendente}
                   AND proxima_tentativa_em <= {agoraUtc}
                 ORDER BY prioridade DESC, criado_em
                 LIMIT {tamanho}
                 FOR UPDATE SKIP LOCKED
                """
            )
            .ToListAsync(ct);

        foreach (var email in lote)
            email.MarcarEmEnvio(agoraUtc);

        return lote;
    }

    /// <inheritdoc />
    /// <remarks>Conta anonimizada ou desativada não recebe, mesmo com a preferência ligada no banco.</remarks>
    public async Task<IReadOnlySet<Guid>> ListarQueRecebemMarketing(IReadOnlyCollection<Guid> usuarioIds, CancellationToken ct = default) =>
        (
            await db
                .Users.AsNoTracking()
                .Where(u => usuarioIds.Contains(u.Id) && u.ReceberComunicacaoDoKapa && u.Ativo && u.AnonimizadoEm == null)
                .Select(u => u.Id)
                .ToListAsync(ct)
        ).ToHashSet();

    /// <inheritdoc />
    /// <remarks><c>ExecuteDeleteAsync</c>: limpeza em massa do worker, sem nada a compor — a mesma exceção de <c>RefreshTokenRepository</c>.</remarks>
    public Task<int> RemoverConcluidosAnterioresA(DateTime limiteUtc, CancellationToken ct = default) =>
        db
            .EmailsFila.Where(e =>
                (e.Status == EEmailStatus.Enviado || e.Status == EEmailStatus.Falhou || e.Status == EEmailStatus.Descartado)
                && e.AtualizadoEm < limiteUtc
            )
            .ExecuteDeleteAsync(ct);

    /// <inheritdoc />
    /// <remarks><c>ExecuteUpdateAsync</c>: correção em massa do worker, pelo mesmo motivo da limpeza.</remarks>
    public Task<int> DesistirDosPresosAnterioresA(DateTime limiteUtc, CancellationToken ct = default) =>
        db
            .EmailsFila.Where(e => e.Status == EEmailStatus.Enviando && e.AtualizadoEm < limiteUtc)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(e => e.Status, EEmailStatus.Falhou)
                        .SetProperty(e => e.UltimoErro, "Envio interrompido: o worker parou no meio.")
                        .SetProperty(e => e.AtualizadoEm, DateTime.UtcNow),
                ct
            );

    /// <inheritdoc />
    /// <remarks>
    /// Três agregações em vez de trazer linhas: é leitura de job, a cada minuto, sobre uma tabela que
    /// pode ter dezenas de milhares de concluídos. <c>AsNoTracking</c> porque nada será alterado.
    /// </remarks>
    public async Task<ProfundidadeDaFilaDeEmail> ContarProfundidade(DateTime limiteDoPreso, CancellationToken ct = default)
    {
        var pendentes = db.EmailsFila.AsNoTracking().Where(e => e.Status == EEmailStatus.Pendente);

        var maisAntigoEm = await pendentes.MinAsync(e => (DateTime?)e.CriadoEm, ct);
        var quantidadeDePendentes = await pendentes.CountAsync(ct);

        var presos = await db.EmailsFila.AsNoTracking().CountAsync(e => e.Status == EEmailStatus.Enviando && e.AtualizadoEm < limiteDoPreso, ct);

        return new ProfundidadeDaFilaDeEmail(quantidadeDePendentes, presos, maisAntigoEm);
    }
}
