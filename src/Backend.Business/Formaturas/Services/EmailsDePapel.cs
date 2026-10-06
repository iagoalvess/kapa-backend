using Backend.Business.Common;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using Backend.Business.Emails.Services;
using Microsoft.Extensions.Options;

namespace Backend.Business.Formaturas.Services;

/// <summary>
/// O link para quem pediu confirmar um Presidente novo (revisão de segurança de 05/10/2026).
/// </summary>
/// <remarks>
/// Presidente troca a conta de recebimento e conecta o Mercado Pago. Sem esta confirmação, quem tinha a senha do
/// presidente promovia uma conta própria e confirmava a troca da chave no e-mail dela. Só enfileira — quem salva é
/// o service.
/// </remarks>
/// <param name="emailService">Fila de e-mails.</param>
/// <param name="aplicacao">Identidade da aplicação.</param>
public sealed class EmailsDePapel(IEmailService emailService, IOptions<AplicacaoSettings> aplicacao)
{
    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    /// <summary>Pede a quem promoveu que confirme o Presidente novo.</summary>
    /// <param name="email">E-mail da conta de quem pediu.</param>
    /// <param name="formatura">Nome da turma.</param>
    /// <param name="nome">Nome de quem vira Presidente.</param>
    /// <param name="emailDoNovo">E-mail de quem vira Presidente — para reconhecer a pessoa.</param>
    /// <param name="token">O token do link.</param>
    public Task ConfirmarPresidente(string email, string formatura, string nome, string emailDoNovo, string token, CancellationToken ct = default) =>
        emailService.Enfileirar(
            new NovoEmail(
                email,
                $"Confirme o novo Presidente — {formatura} — {_aplicacao.Nome}",
                ModeloDeEmail.Montar(
                    _aplicacao,
                    "Confirme o novo Presidente",
                    $"Você pediu para <strong>{ModeloDeEmail.Texto(nome)}</strong> ({ModeloDeEmail.Texto(emailDoNovo)}) virar Presidente de "
                        + $"<strong>{ModeloDeEmail.Texto(formatura)}</strong>. Presidente troca a conta para onde a turma paga e conecta o "
                        + $"Mercado Pago.<br><br>O link vale {(int)ConfirmacaoPorEmail.Validade.TotalMinutes} minutos. "
                        + "<strong>Se não foi você, não clique</strong>: troque sua senha agora e avise a comissão.",
                    "Confirmar o novo Presidente",
                    _aplicacao.MontarUrl(RotasDoFront.ConfirmarPresidente, [new("token", token)]),
                    Mascote.Lupa
                )
            ),
            ct
        );
}
