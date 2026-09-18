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
/// <c>EmailsDeAdesao</c>. Sem botão: parte da comissão não abre a tela da chave, e o e-mail já traz
/// tudo o que é preciso para reconhecer uma troca indevida.
/// </remarks>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="aplicacao">Identidade da aplicação.</param>
public sealed class EmailsDeRecebimento(IEmailService emailService, IOptions<AplicacaoSettings> aplicacao)
{
    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    /// <summary>A chave, o titular ou a cidade mudou: para onde o dinheiro vai agora, e quem mudou.</summary>
    /// <param name="email">Membro da comissão.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="autor">Nome de quem trocou.</param>
    /// <param name="conta">A conta nova, já gravada.</param>
    public Task ContaAlterada(string email, string formatura, string autor, ContaDeRecebimento conta, CancellationToken ct = default)
    {
        var mensagem =
            $"{ModeloDeEmail.Texto(autor)} alterou a conta de recebimento de <strong>{ModeloDeEmail.Texto(formatura)}</strong>. "
            + $"A partir de agora, os pagamentos da turma vão para a chave {ModeloDeEmail.Texto(ChavePix.Rotulo(conta.TipoDeChave))} "
            + $"<strong>{ModeloDeEmail.Texto(conta.Chave)}</strong>, em nome de <strong>{ModeloDeEmail.Texto(conta.NomeDoTitular)}</strong>. "
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
}
