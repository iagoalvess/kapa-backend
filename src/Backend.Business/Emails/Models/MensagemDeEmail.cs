namespace Backend.Business.Emails.Models;

/// <summary>
/// Pedido de envio, como a aplicação o descreve.
/// </summary>
/// <param name="Para">Endereço do destinatário.</param>
/// <param name="Assunto">Assunto da mensagem.</param>
/// <param name="CorpoHtml">Corpo em HTML.</param>
/// <param name="Prioridade">Ordem de atendimento na fila.</param>
public sealed record NovoEmail(string Para, string Assunto, string CorpoHtml, EEmailPrioridade Prioridade = EEmailPrioridade.Normal);

/// <summary>
/// Mensagem pronta para entregar ao servidor de e-mail.
/// </summary>
/// <remarks>
/// Separada de <see cref="NovoEmail"/> porque é o contrato do <c>IEmailSender</c>, que não
/// conhece fila nem prioridade — só sabe entregar uma mensagem.
/// </remarks>
/// <param name="Para">Endereço do destinatário.</param>
/// <param name="Assunto">Assunto da mensagem.</param>
/// <param name="CorpoHtml">Corpo em HTML.</param>
public sealed record MensagemDeEmail(string Para, string Assunto, string CorpoHtml);
