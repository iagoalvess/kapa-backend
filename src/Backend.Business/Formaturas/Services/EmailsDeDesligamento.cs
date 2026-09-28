using Backend.Business.Common;
using Backend.Business.Common.Texto;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Formaturas.Models;
using Microsoft.Extensions.Options;

namespace Backend.Business.Formaturas.Services;

/// <summary>
/// Os e-mails da saída: a confirmação para quem saiu e o aviso para a comissão.
/// </summary>
/// <remarks>
/// Só enfileira — quem salva é o service, na mesma transação do desligamento. Sem interface, como
/// <c>EmailsDeAdesao</c>: uma implementação, e o teste a constrói com um <c>IEmailService</c> substituto.
/// <para>
/// A mensagem de quem sai diz o que foi cancelado <b>e</b> o que não volta: a devolução, se houver, é
/// combinada com a comissão, porque o dinheiro nunca passou pela Kapa (P2 de 17/09/2026). É a
/// pergunta que a pessoa faz em seguida, e ela precisa da resposta antes de escrever de volta.
/// </para>
/// </remarks>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="aplicacao">Identidade da aplicação.</param>
public sealed class EmailsDeDesligamento(IEmailService emailService, IOptions<AplicacaoSettings> aplicacao)
{
    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    /// <summary>O extrato do próprio formando — a tela que continua aberta para ele depois da saída.</summary>
    private string LinkDoExtrato => _aplicacao.Link(RotasDoFront.MinhasParcelas);

    /// <summary>A lista de membros, onde a comissão vê o selo e pode desfazer.</summary>
    private string LinkDosMembros => _aplicacao.Link(RotasDoFront.MembrosDaFormatura);

    /// <summary>Confirmação para quem saiu: o que deixou de dever, o que já pagou e o que não volta.</summary>
    /// <param name="email">Quem saiu.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="cancelado">Parcelas canceladas e quanto somavam.</param>
    /// <param name="jaPagoEmCentavos">Quanto já havia entrado na conta da turma por ele.</param>
    public Task Confirmacao(string email, string formatura, CancelamentoDaSaida cancelado, long jaPagoEmCentavos, CancellationToken ct = default)
    {
        var baixa =
            cancelado.Parcelas == 0
                ? "Você não tinha parcela em aberto."
                : $"Deixaram de ser devidas {cancelado.Parcelas} parcela(s), no total de "
                    + $"<strong>{FormatosBrasileiros.Reais(cancelado.ValorEmCentavos)}</strong>.";

        var pago =
            jaPagoEmCentavos == 0
                ? string.Empty
                : $" O que você já pagou — {FormatosBrasileiros.Reais(jaPagoEmCentavos)} — continua no caixa da turma; "
                    + "devolução, se houver, é combinada diretamente com a comissão.";

        return Enfileirar(
            email,
            $"Você foi desligado de {formatura}",
            "Desligamento registrado",
            $"A comissão de <strong>{ModeloDeEmail.Texto(formatura)}</strong> registrou a sua saída da turma. {baixa}{pago} "
                + "O seu extrato continua disponível — ele é o comprovante do que você pagou.",
            "Ver meu extrato",
            LinkDoExtrato,
            Mascote.Erro,
            ct
        );
    }

    /// <summary>Aviso para a comissão: quem saiu, por quê, e quanto deixou de entrar.</summary>
    /// <param name="emails">Destinatários — a comissão da turma.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="nome">Quem saiu.</param>
    /// <param name="motivo">Motivo da lista de <see cref="MotivoDeSaida"/>.</param>
    /// <param name="cancelado">Parcelas canceladas e quanto somavam.</param>
    public async Task Aviso(
        IReadOnlyList<string> emails,
        string formatura,
        string nome,
        string motivo,
        CancelamentoDaSaida cancelado,
        CancellationToken ct = default
    )
    {
        var mensagem =
            $"<strong>{ModeloDeEmail.Texto(nome)}</strong> foi desligado de "
            + $"<strong>{ModeloDeEmail.Texto(formatura)}</strong>. Motivo: {ModeloDeEmail.Texto(motivo)}. "
            + $"Saíram da projeção {cancelado.Parcelas} parcela(s), no total de "
            + $"<strong>{FormatosBrasileiros.Reais(cancelado.ValorEmCentavos)}</strong>.";

        foreach (var email in emails)
            await Enfileirar(
                email,
                $"Saída de formando — {formatura}",
                "Formando desligado",
                mensagem,
                "Ver membros",
                LinkDosMembros,
                Mascote.Alerta,
                ct
            );
    }

    private async Task Enfileirar(
        string email,
        string assunto,
        string titulo,
        string mensagem,
        string botao,
        string link,
        Mascote mascote,
        CancellationToken ct
    ) =>
        await emailService.Enfileirar(
            new NovoEmail(email, $"{assunto} — {_aplicacao.Nome}", ModeloDeEmail.Montar(_aplicacao, titulo, mensagem, botao, link, mascote)),
            ct
        );
}
