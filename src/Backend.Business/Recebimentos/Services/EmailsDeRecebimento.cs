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
                ModeloDeEmail.Montar(_aplicacao.Nome, "Conta de recebimento alterada", mensagem, null, null, Mascote.Alerta)
            ),
            ct
        );
    }

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
