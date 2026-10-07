using System.Globalization;
using Backend.Business.Common;
using Backend.Business.Common.Texto;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Pagamentos.Models;
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
    private string LinkDoExtrato => _aplicacao.Link(RotasDoFront.MinhasParcelas);

    /// <summary>
    /// A tesouraria achou o dinheiro e registrou o pagamento: um e-mail por formando, com todas as parcelas dele.
    /// </summary>
    /// <remarks>
    /// Com saldo, a parcela continua em aberto e o e-mail diz quanto falta: é a diferença entre "está
    /// tudo certo" e "a tesouraria achou o seu PIX, mas você ainda deve" — e a pessoa precisa saber
    /// qual das duas antes do próximo vencimento.
    /// <para>
    /// O botão leva ao recibo desta baixa (Sprint 22), e não ao extrato: é o e-mail que a pessoa guarda,
    /// e o recibo é a prova. O PDF não vai anexado — 80 anexos por mês que ninguém pediu; o link basta.
    /// </para>
    /// <para>
    /// Várias parcelas do mesmo formando — o PIX de várias parcelas, a conferência em lote — viram um e-mail só
    /// (07/10/2026), com o recibo de cada uma na lista e o botão no extrato. Antes era um e-mail igual por parcela.
    /// </para>
    /// </remarks>
    /// <param name="confirmados">As parcelas baixadas, de um ou mais formandos.</param>
    public async Task Confirmados(IEnumerable<PagamentoConfirmado> confirmados, CancellationToken ct = default)
    {
        foreach (var doFormando in confirmados.GroupBy(c => (c.Email, c.Turma)))
        {
            var itens = doFormando.OrderBy(c => c.Vencimento).ToList();
            var turma = doFormando.Key.Turma;
            var parcial = itens.Any(c => c.SaldoEmCentavos > 0);

            if (itens is [var unico])
            {
                await Enfileirar(
                    unico.Email,
                    parcial ? $"Pagamento parcial registrado — {turma}" : $"Pagamento confirmado — {turma}",
                    parcial ? "Pagamento parcial registrado" : "Pagamento confirmado",
                    $"A tesouraria de <strong>{ModeloDeEmail.Texto(turma)}</strong> confirmou o pagamento de "
                        + $"{FormatosBrasileiros.Reais(unico.ValorEmCentavos)}, feito em {Dia(unico.PagoEm)}, da sua parcela com vencimento em {Dia(unico.Vencimento)}."
                        + Saldo(unico),
                    Mascote.Cofrinho,
                    ("Ver o recibo", Recibo(unico)),
                    ct
                );
                continue;
            }

            var linhas = itens.Select(c =>
                $"""<li>Vencimento em {Dia(c.Vencimento)}: {FormatosBrasileiros.Reais(c.ValorEmCentavos)}.{Saldo(c)} <a href="{Recibo(c)}">Ver o recibo</a></li>"""
            );

            await Enfileirar(
                doFormando.Key.Email,
                $"Pagamentos confirmados — {turma}",
                "Pagamentos confirmados",
                $"A tesouraria de <strong>{ModeloDeEmail.Texto(turma)}</strong> confirmou o pagamento de {itens.Count} parcelas, "
                    + $"no total de {FormatosBrasileiros.Reais(itens.Sum(c => c.ValorEmCentavos))}."
                    + $"""<ul style="line-height:1.8;margin:16px 0 0;padding-left:20px">{string.Concat(linhas)}</ul>""",
                Mascote.Cofrinho,
                null,
                ct
            );
        }
    }

    private static string Saldo(PagamentoConfirmado confirmado) =>
        confirmado.SaldoEmCentavos > 0
            ? $" Ela continua em aberto: ainda faltam <strong>{FormatosBrasileiros.Reais(confirmado.SaldoEmCentavos)}</strong>."
            : string.Empty;

    private string Recibo(PagamentoConfirmado confirmado) => _aplicacao.Link($"{RotasDoFront.Recibo}{confirmado.RecebimentoId}");

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
            Mascote.Erro,
            null,
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
            Mascote.Alerta,
            null,
            ct
        );

    /// <summary>
    /// O Mercado Pago tirou da conta da turma um pagamento que já tinha baixado — contestação no cartão ou
    /// devolução pelo painel —, e o Kapa estornou sozinho (Sprint 39, P4). Para a comissão.
    /// </summary>
    /// <param name="email">Membro da comissão.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="motivo">"contestação no cartão" ou "devolvido no Mercado Pago".</param>
    /// <param name="valorEmCentavos">O valor do pagamento.</param>
    /// <param name="oQue">O que foi desfeito ("2 parcelas de Fulana", "a compra A1B2C3D4 da loja").</param>
    public Task DevolvidoNoMercadoPago(
        string email,
        string formatura,
        string motivo,
        long valorEmCentavos,
        string oQue,
        CancellationToken ct = default
    ) =>
        Enfileirar(
            email,
            $"Pagamento estornado pelo Mercado Pago — {formatura}",
            "Pagamento estornado pelo Mercado Pago",
            $"Um pagamento de {FormatosBrasileiros.Reais(valorEmCentavos)} para <strong>{ModeloDeEmail.Texto(formatura)}</strong> "
                + $"saiu da conta Mercado Pago da turma — motivo: <em>{ModeloDeEmail.Texto(motivo)}</em>. "
                + $"O Kapa desfez sozinho {ModeloDeEmail.Texto(oQue)}, que volta a ficar em aberto. "
                + "Se a turma contestar e ganhar, registre o pagamento de novo pela baixa manual.",
            Mascote.Alerta,
            ("Ver as parcelas", _aplicacao.Link(RotasDoFront.ParcelasDaTurma)),
            ct
        );

    private static string Dia(DateOnly dia) => dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Enfileira o aviso no modelo da casa; sem botão próprio, o botão leva ao extrato.</summary>
    private async Task Enfileirar(
        string email,
        string assunto,
        string titulo,
        string mensagem,
        Mascote mascote,
        (string Texto, string Link)? botao,
        CancellationToken ct
    ) =>
        await emailService.Enfileirar(
            new NovoEmail(
                email,
                $"{assunto} — {_aplicacao.Nome}",
                ModeloDeEmail.Montar(_aplicacao, titulo, mensagem, botao?.Texto ?? "Ver meu extrato", botao?.Link ?? LinkDoExtrato, mascote)
            ),
            ct
        );
}
