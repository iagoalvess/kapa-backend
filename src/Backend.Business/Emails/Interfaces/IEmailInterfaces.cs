using Backend.Business.Abstractions;
using Backend.Business.Emails.Models;

namespace Backend.Business.Emails.Interfaces;

/// <summary>
/// Entrega uma mensagem ao servidor de e-mail.
/// </summary>
/// <remarks>
/// É o único ponto que conhece o provedor. Lança em caso de falha — quem trata é o job de
/// envio, que sabe reagendar.
/// </remarks>
public interface IEmailSender
{
    /// <summary>Entrega a mensagem.</summary>
    /// <param name="mensagem">Mensagem a enviar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <exception cref="Exception">Qualquer falha de conexão, autenticação ou recusa do servidor.</exception>
    Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct = default);
}

/// <summary>
/// Enfileira e-mails.
/// </summary>
/// <remarks>
/// É isto que a aplicação usa. Nenhum service de domínio chama <see cref="IEmailSender"/>
/// diretamente: e-mail enviado dentro da requisição HTTP amarra o tempo de resposta ao humor do
/// servidor SMTP, e some sem rastro se o provedor estiver fora no momento exato.
/// </remarks>
public interface IEmailService
{
    /// <summary>Coloca um e-mail na fila de envio.</summary>
    /// <param name="dados">Destinatário, assunto, corpo e prioridade.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>O identificador do e-mail na fila.</returns>
    Task<Result<Guid>> Enfileirar(NovoEmail dados, CancellationToken ct = default);
}

/// <summary>
/// Acesso à fila de e-mails.
/// </summary>
public interface IEmailFilaRepository
{
    /// <summary>Marca um e-mail para inclusão na fila.</summary>
    /// <param name="email">E-mail a enfileirar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task Adicionar(EmailNaFila email, CancellationToken ct = default);

    /// <summary>
    /// Reserva um lote de e-mails pendentes para este processo.
    /// </summary>
    /// <remarks>
    /// Deve ser chamado dentro de uma transação (<c>IUnitOfWork.EmTransacaoAsync</c>): a reserva
    /// usa <c>FOR UPDATE SKIP LOCKED</c>, que é o que impede duas réplicas do worker de pegarem
    /// a mesma linha e enviarem o e-mail duas vezes.
    /// </remarks>
    /// <param name="tamanho">Quantidade máxima de e-mails.</param>
    /// <param name="agoraUtc">Momento da reserva.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<IReadOnlyList<EmailNaFila>> ReservarLote(int tamanho, DateTime agoraUtc, CancellationToken ct = default);
}
