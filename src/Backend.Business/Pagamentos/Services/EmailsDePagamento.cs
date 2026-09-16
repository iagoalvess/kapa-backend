using System.Globalization;
using Backend.Business.Common;
using Backend.Business.Common.Texto;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Microsoft.Extensions.Options;

namespace Backend.Business.Pagamentos.Services;

/// <summary>
/// Monta e enfileira os avisos do pagamento ao formando: confirmado e recusado.
/// </summary>
/// <remarks>
/// Só enfileira — quem salva é o service, na mesma transação da baixa ou da recusa. Sem interface,
/// como <c>EmailsDeAdesao</c>. É este e-mail que substitui o poll: o formando avisou e fechou a tela;
/// quem conta o que aconteceu é a caixa de entrada.
/// </remarks>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="aplicacao">Identidade da aplicação.</param>
public sealed class EmailsDePagamento(IEmailService emailService, IOptions<AplicacaoSettings> aplicacao)
{
    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    /// <summary>O extrato no front — o mesmo caminho de <c>ROTAS.extrato</c>.</summary>
    private string LinkDoExtrato => $"{_aplicacao.UrlDoFrontend.TrimEnd('/')}/extrato";

    /// <summary>A tesouraria achou o dinheiro e baixou a parcela.</summary>
    /// <param name="email">Formando.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="vencimento">Vencimento da parcela.</param>
    /// <param name="valorEmCentavos">O que entrou.</param>
    /// <param name="pagoEm">Dia em que entrou.</param>
    public Task Confirmado(
        string email,
        string formatura,
        DateOnly vencimento,
        long valorEmCentavos,
        DateOnly pagoEm,
        CancellationToken ct = default
    ) =>
        Enfileirar(
            email,
            $"Pagamento confirmado — {formatura}",
            "Pagamento confirmado",
            $"A tesouraria de <strong>{ModeloDeEmail.Texto(formatura)}</strong> confirmou o pagamento de "
                + $"{FormatosBrasileiros.Reais(valorEmCentavos)}, feito em {Dia(pagoEm)}, da sua parcela com vencimento em {Dia(vencimento)}.",
            ct
        );

    /// <summary>A tesouraria não achou o dinheiro: o motivo, e a parcela continua em aberto.</summary>
    /// <param name="email">Formando.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="vencimento">Vencimento da parcela.</param>
    /// <param name="valorEmCentavos">Valor informado.</param>
    /// <param name="motivo">O que a tesouraria escreveu.</param>
    public Task Recusado(string email, string formatura, DateOnly vencimento, long valorEmCentavos, string motivo, CancellationToken ct = default) =>
        Enfileirar(
            email,
            $"Pagamento não confirmado — {formatura}",
            "Pagamento não confirmado",
            $"A tesouraria de <strong>{ModeloDeEmail.Texto(formatura)}</strong> não confirmou o pagamento de "
                + $"{FormatosBrasileiros.Reais(valorEmCentavos)} que você informou para a parcela com vencimento em {Dia(vencimento)}. "
                + $"Motivo: <em>{ModeloDeEmail.Texto(motivo)}</em>. A parcela continua em aberto — se você pagou, fale com a tesouraria.",
            ct
        );

    /// <summary>A baixa foi desfeita: a parcela voltou a ser devida, e o formando fica sabendo por quê.</summary>
    /// <remarks>
    /// Com a justificativa, como na recusa: quem vai pagar de novo precisa saber o motivo, e o silêncio
    /// de uma parcela que "despagou" sozinha é o que vira ligação para a comissão.
    /// </remarks>
    /// <param name="email">Formando.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="vencimento">Vencimento da parcela.</param>
    /// <param name="valorEmCentavos">O que havia sido baixado.</param>
    /// <param name="justificativa">O que o Presidente escreveu.</param>
    public Task Estornado(
        string email,
        string formatura,
        DateOnly vencimento,
        long valorEmCentavos,
        string justificativa,
        CancellationToken ct = default
    ) =>
        Enfileirar(
            email,
            $"Pagamento estornado — {formatura}",
            "Pagamento estornado",
            $"A comissão de <strong>{ModeloDeEmail.Texto(formatura)}</strong> desfez a baixa de "
                + $"{FormatosBrasileiros.Reais(valorEmCentavos)} da sua parcela com vencimento em {Dia(vencimento)}. "
                + $"Motivo: <em>{ModeloDeEmail.Texto(justificativa)}</em>. A parcela voltou a ficar em aberto — "
                + "se você não reconhece este estorno, fale com a comissão.",
            ct
        );

    private static string Dia(DateOnly dia) => dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private async Task Enfileirar(string email, string assunto, string titulo, string mensagem, CancellationToken ct) =>
        await emailService.Enfileirar(
            new NovoEmail(
                email,
                $"{assunto} — {_aplicacao.Nome}",
                ModeloDeEmail.Montar(_aplicacao.Nome, titulo, mensagem, "Ver meu extrato", LinkDoExtrato)
            ),
            ct
        );
}
