namespace Backend.Business.Notificacoes.Models;

/// <summary>
/// Uma mensagem pronta, como o canal a recebe.
/// </summary>
/// <remarks>
/// O canal não conhece régua, parcela nem formatura: recebe destinatário, assunto e corpo. É o que
/// permite o WhatsApp entrar como segunda implementação sem tocar no <c>ReguaService</c>.
/// </remarks>
/// <param name="Para">Endereço do destinatário, no formato do canal.</param>
/// <param name="Assunto">Assunto, já com as variáveis trocadas.</param>
/// <param name="CorpoHtml">Corpo em HTML, já com as variáveis trocadas e escapadas.</param>
/// <param name="Link">Destino do botão da mensagem, se houver.</param>
/// <param name="TextoDoLink">Texto do botão.</param>
public sealed record MensagemDeNotificacao(string Para, string Assunto, string CorpoHtml, string? Link = null, string? TextoDoLink = null);

/// <summary>O que o canal devolve depois de aceitar (ou recusar) a mensagem.</summary>
/// <param name="ReferenciaExterna">Identificador da mensagem no canal — na fila de e-mail, o id da linha.</param>
public sealed record EntregaDaMensagem(Guid? ReferenciaExterna);
