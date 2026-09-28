using Backend.Business.Abstractions;
using Backend.Business.Common;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;
using Microsoft.Extensions.Options;

namespace Backend.Business.Notificacoes.Services;

/// <summary>
/// O canal desta sprint: põe a mensagem na fila de e-mails que o worker já esvazia.
/// </summary>
/// <remarks>
/// Não envia — enfileira, como todo e-mail do projeto. Devolve o id da linha da fila, e é por ele
/// que o histórico descobre depois se a mensagem foi aceita ou se o endereço é inválido.
/// </remarks>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="aplicacao">Identidade da aplicação, para o rodapé e o nome no assunto.</param>
public sealed class CanalDeEmail(IEmailService emailService, IOptions<AplicacaoSettings> aplicacao) : ICanalDeNotificacao
{
    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    /// <inheritdoc />
    public CanalDeNotificacao Canal => CanalDeNotificacao.Email;

    /// <inheritdoc />
    public async Task<Result<EntregaDaMensagem>> Enviar(MensagemDeNotificacao mensagem, CancellationToken ct = default)
    {
        var corpo = ModeloDeEmail.Montar(_aplicacao, mensagem.Assunto, mensagem.CorpoHtml, mensagem.TextoDoLink, mensagem.Link, Mascote.Cofrinho);

        var enfileirado = await emailService.Enfileirar(new NovoEmail(mensagem.Para, $"{mensagem.Assunto} — {_aplicacao.Nome}", corpo), ct);

        return enfileirado.Sucesso ? new EntregaDaMensagem(enfileirado.Valor) : Result.Falha<EntregaDaMensagem>(enfileirado.Erros);
    }
}
