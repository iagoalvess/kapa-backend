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
}
