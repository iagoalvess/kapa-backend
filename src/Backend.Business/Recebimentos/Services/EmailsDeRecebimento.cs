using Backend.Business.Common;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Backend.Business.Recebimentos.Models;
using Microsoft.Extensions.Options;

namespace Backend.Business.Recebimentos.Services;

/// <summary>
/// Monta e enfileira o aviso de troca da conta de recebimento.
/// </summary>
/// <remarks>
/// Só enfileira — quem salva é o service, na mesma transação da troca. Sem interface, como
/// <c>EmailsDeAdesao</c>. Sem botão: parte da comissão não abre a tela dos meios, e o e-mail já traz
/// tudo o que é preciso para reconhecer uma troca indevida.
/// </remarks>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="aplicacao">Identidade da aplicação.</param>
public sealed class EmailsDeRecebimento(IEmailService emailService, IOptions<AplicacaoSettings> aplicacao)
{
    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    /// <summary>
    /// Algum meio mudou: o que deixou de valer, por onde o dinheiro entra agora, e quem mudou.
    /// </summary>
    /// <remarks>
    /// O que <b>saiu</b> vem primeiro, e é o motivo de este e-mail existir. A fraude desta sprint é
    /// desligar o PIX da comissão e ligar "dinheiro com fulano": mostrando só o depois, quem lê no
    /// celular vê uma lista plausível e não percebe que a chave da turma sumiu. A remoção é o sinal
    /// mais forte de que algo está errado, e por isso abre a mensagem.
    /// <para>
    /// Cada meio aparece pelo que o expõe — o PIX pela chave e pelo titular, a transferência pela
    /// conta, o dinheiro por quem recebe. Quem lê precisa reconhecer a alteração, e para isso o
    /// destino tem de vir por extenso.
    /// </para>
    /// </remarks>
    /// <param name="email">Membro da comissão.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="autor">Nome de quem trocou.</param>
    /// <param name="antes">Os meios como estavam.</param>
    /// <param name="depois">Os meios novos, já gravados.</param>
    public Task ContaAlterada(string email, string formatura, string autor, MeiosDaConta antes, MeiosDaConta depois, CancellationToken ct = default)
    {
        var saiu = Saidas(antes, depois);

        var mensagem =
            $"{ModeloDeEmail.Texto(autor)} alterou a conta de recebimento de <strong>{ModeloDeEmail.Texto(formatura)}</strong>.<br><br>"
            + (saiu.Length == 0 ? string.Empty : $"<strong>Deixou de valer:</strong><br>{saiu}<br><br>")
            + $"<strong>A partir de agora, a turma recebe assim:</strong><br>{Linhas(depois)}<br><br>"
            + "Se você não reconhece esta alteração, fale com a comissão antes que alguém pague.";

        return emailService.Enfileirar(
            new NovoEmail(
                email,
                $"Conta de recebimento alterada — {formatura} — {_aplicacao.Nome}",
                ModeloDeEmail.Montar(_aplicacao, "Conta de recebimento alterada", mensagem, null, null, Mascote.Lupa)
            ),
            ct
        );
    }

    /// <summary>
    /// O Mercado Pago da turma foi conectado, trocado ou desconectado (Sprint 25, P3).
    /// </summary>
    /// <remarks>
    /// Mesmo motivo do aviso de troca de conta: é para onde vai o dinheiro do PIX automático. Conectar a
    /// conta errada desvia a mensalidade tanto quanto trocar a chave, e a comissão precisa ver a conta
    /// que entrou.
    /// </remarks>
    /// <param name="email">Membro da comissão.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="autor">Nome de quem conectou ou desconectou.</param>
    /// <param name="conta">A conta do Mercado Pago que entrou; nula quando desconectou.</param>
    public Task ProvedorAlterado(string email, string formatura, string autor, string? conta, CancellationToken ct = default)
    {
        var titulo = conta is null ? "Mercado Pago desconectado" : "Mercado Pago conectado";
        var mensagem = conta is null
            ? $"{ModeloDeEmail.Texto(autor)} desconectou o Mercado Pago de <strong>{ModeloDeEmail.Texto(formatura)}</strong>. "
                + "O PIX com confirmação automática saiu da tela de pagamento; os outros meios continuam valendo."
            : $"{ModeloDeEmail.Texto(autor)} conectou a conta <strong>{ModeloDeEmail.Texto(conta)}</strong> do Mercado Pago a "
                + $"<strong>{ModeloDeEmail.Texto(formatura)}</strong>. A partir de agora, o PIX com confirmação automática cai nessa conta.";

        return emailService.Enfileirar(
            new NovoEmail(
                email,
                $"{titulo} — {formatura} — {_aplicacao.Nome}",
                ModeloDeEmail.Montar(
                    _aplicacao,
                    titulo,
                    $"{mensagem}<br><br>Se você não reconhece esta alteração, fale com a comissão antes que alguém pague.",
                    null,
                    null,
                    conta is null ? Mascote.Lupa : Mascote.Feliz
                )
            ),
            ct
        );
    }

    /// <summary>
    /// O aviso à turma: a conta para onde ela paga mudou, e como conferir (Sprint 22, P1).
    /// </summary>
    /// <remarks>
    /// Revisa a P2 da Sprint 8, que avisava só a comissão para não alarmar 80 pessoas: desviar a
    /// mensalidade é o pior cenário do produto, e 80 pessoas sabendo é a defesa mais barata contra
    /// ele. <b>Não</b> traz a chave nem a conta novas — um e-mail com dados de pagamento é exatamente o
    /// que o golpe forjaria. Diz o que mudou, onde conferir (a tela de pagamento, que mostra o titular
    /// e a conferência) e com quem falar.
    /// </remarks>
    /// <param name="email">Formando.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="autor">Nome de quem trocou.</param>
    public Task ContaAlteradaParaATurma(string email, string formatura, string autor, CancellationToken ct = default) =>
        emailService.Enfileirar(
            new NovoEmail(
                email,
                $"A conta de pagamento da turma mudou — {formatura} — {_aplicacao.Nome}",
                ModeloDeEmail.Montar(
                    _aplicacao,
                    "A conta de pagamento da turma mudou",
                    $"{ModeloDeEmail.Texto(autor)} alterou a conta para onde <strong>{ModeloDeEmail.Texto(formatura)}</strong> paga as parcelas.<br><br>"
                        + "Antes de pagar a próxima, abra a parcela no app: o nome de quem recebe aparece acima do QR, com a data em que a "
                        + "comissão conferiu a conta no banco. <strong>Pague só pelo que aparece no app</strong> — nunca por dados recebidos "
                        + "por mensagem.<br><br>Se você não esperava esta mudança, fale com a comissão antes de pagar.",
                    "Ver minhas parcelas",
                    _aplicacao.Link(RotasDoFront.MinhasParcelas),
                    Mascote.Lupa
                )
            ),
            ct
        );

    /// <summary>
    /// O que deixou de valer: o meio que saiu, e o que continua mas com outro destino.
    /// </summary>
    /// <remarks>
    /// Meio que mudou de conteúdo aparece nos dois blocos — a chave antiga em "deixou de valer", a
    /// nova embaixo. É a leitura que a Sprint 8 já dava para a troca de chave, e a que deixa a
    /// diferença à vista sem obrigar ninguém a comparar duas listas.
    /// </remarks>
    /// <param name="antes">Os meios como estavam.</param>
    /// <param name="depois">Os meios novos.</param>
    private static string Saidas(MeiosDaConta antes, MeiosDaConta depois)
    {
        var mudou = new MeiosDaConta(
            antes.Pix == depois.Pix ? null : antes.Pix,
            antes.Transferencia == depois.Transferencia ? null : antes.Transferencia,
            antes.Dinheiro == depois.Dinheiro ? null : antes.Dinheiro
        );

        return Linhas(mudou);
    }

    /// <summary>Uma linha por meio habilitado, com o destino por extenso.</summary>
    /// <param name="meios">Meios a descrever.</param>
    private static string Linhas(MeiosDaConta meios)
    {
        var linhas = new List<string>(3);

        if (meios.Pix is { } pix)
            linhas.Add(
                $"• <strong>PIX</strong> — chave {ModeloDeEmail.Texto(ChavePix.Rotulo(pix.TipoDeChave))} "
                    + $"<strong>{ModeloDeEmail.Texto(pix.Chave)}</strong>, em nome de <strong>{ModeloDeEmail.Texto(pix.NomeDoTitular)}</strong>"
            );

        if (meios.Transferencia is { } conta)
            linhas.Add(
                $"• <strong>Transferência</strong> — {ModeloDeEmail.Texto(conta.Banco)}, agência {ModeloDeEmail.Texto(conta.Agencia)}, "
                    + $"conta {ModeloDeEmail.Texto(conta.Conta)} ({ModeloDeEmail.Texto(conta.TipoDeConta)}), "
                    + $"em nome de <strong>{ModeloDeEmail.Texto(conta.Titular)}</strong>"
            );

        if (meios.Dinheiro is { } dinheiro)
            linhas.Add($"• <strong>Dinheiro</strong> — com <strong>{ModeloDeEmail.Texto(dinheiro.Nome)}</strong>");

        return string.Join("<br>", linhas);
    }
}
