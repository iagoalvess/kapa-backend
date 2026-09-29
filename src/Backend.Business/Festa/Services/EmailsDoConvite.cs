using System.Globalization;
using Backend.Business.Common;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Festa.Models;
using Microsoft.Extensions.Options;

namespace Backend.Business.Festa.Services;

/// <summary>
/// Os e-mails que vão para o <b>convidado</b>: o convite, e o aviso de que o dele foi cancelado.
/// </summary>
/// <remarks>
/// É o "atribuir ingresso" das plataformas de mercado, sem conta (decisão 17). Só enfileira — entra
/// na transação de quem nomeou ou transferiu. Sem interface, como <c>EmailsDeAdesao</c>.
/// </remarks>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="aplicacao">Identidade da aplicação e o endereço do front.</param>
public sealed class EmailsDoConvite(IEmailService emailService, IOptions<AplicacaoSettings> aplicacao)
{
    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    /// <summary>O link da página pública do convite.</summary>
    /// <param name="token">Código com a assinatura.</param>
    public string Link(string token) => _aplicacao.Link($"{RotasDoFront.Ingresso}{token}");

    /// <summary>O convite, com o link que abre o QR e o PDF anexado.</summary>
    /// <remarks>
    /// O PDF vai junto (23/09/2026, decisão do dono do produto): é o convite que abre sem internet na
    /// porta do salão (decisão 7) e o que se imprime direto do e-mail. É o mesmo arquivo da rota
    /// pública <c>/pdf</c>, montado agora e guardado com a mensagem na fila — ver <see cref="AnexoDoEmail"/>.
    /// O link continua sendo o caminho principal: a página lê o evento a cada abertura, e o PDF é a
    /// foto do convite no dia do envio.
    /// </remarks>
    /// <param name="email">Convidado.</param>
    /// <param name="convite">O convite como a página pública o mostra — documento já mascarado.</param>
    public Task Enviado(string email, ConvitePublico convite, CancellationToken ct = default)
    {
        var evento = convite.Evento;
        var link = Link(convite.Token);
        var quando =
            $"{evento.Data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}{(evento.Hora is { } hora ? $" às {hora.ToString("HH:mm", CultureInfo.InvariantCulture)}" : string.Empty)}";

        return emailService.Enfileirar(
            new NovoEmail(
                email,
                $"Seu convite: {evento.Titulo} — {_aplicacao.Nome}",
                ModeloDeEmail.Montar(
                    _aplicacao,
                    "Você tem um convite",
                    $"<strong>{ModeloDeEmail.Texto(convite.NomeDoConvidado)}</strong>, este é o seu convite para <strong>{ModeloDeEmail.Texto(evento.Titulo)}</strong>, "
                        + $"em {ModeloDeEmail.Texto(quando)}{(evento.Local is { } local ? $", {ModeloDeEmail.Texto(local)}" : string.Empty)}. "
                        + "Na entrada, apresente o QR do convite e um documento com foto. O convite também vai em PDF, anexo, para abrir sem internet ou imprimir.",
                    "Abrir o convite",
                    link,
                    Mascote.Acenando
                ),
                Anexo: new AnexoDoEmail($"convite-{convite.Codigo}.pdf", "application/pdf", ConviteEmPdf.Gerar(convite, link))
            ),
            ct
        );
    }

    /// <summary>O convite que esta pessoa tinha deixou de valer — o QR antigo não entra mais.</summary>
    /// <param name="email">Convidado.</param>
    /// <param name="evento">O evento.</param>
    /// <param name="porque">
    /// O que aconteceu, em HTML já escapado; nulo é a transferência — quem comprou passou o convite para outra pessoa.
    /// </param>
    public Task Cancelado(string email, EventoDoConvite evento, string? porque = null, CancellationToken ct = default) =>
        emailService.Enfileirar(
            new NovoEmail(
                email,
                $"Seu convite foi cancelado: {evento.Titulo} — {_aplicacao.Nome}",
                ModeloDeEmail.Montar(
                    _aplicacao,
                    "Seu convite foi cancelado",
                    $"O convite em seu nome para <strong>{ModeloDeEmail.Texto(evento.Titulo)}</strong> "
                        + (porque ?? "foi transferido para outra pessoa por quem o comprou")
                        + ", e o código não vale mais na portaria. Se isto é um engano, fale com quem te convidou.",
                    null,
                    null,
                    Mascote.Erro
                )
            ),
            ct
        );
}
