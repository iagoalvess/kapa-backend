using Backend.Business.Abstractions;
using Backend.Business.Emails.Interfaces;
using Backend.Business.Emails.Models;
using FluentValidation;

namespace Backend.Business.Emails.Services;

/// <summary>
/// Enfileira e-mails. Nunca envia.
/// </summary>
/// <remarks>
/// Repare que este service não conhece <c>IEmailSender</c>. A separação é proposital: quem
/// origina o e-mail (um cadastro, uma redefinição de senha) não deve ter como enviá-lo na hora
/// nem por engano.
/// </remarks>
/// <param name="emailFilaRepository">Acesso à fila.</param>
/// <param name="validator">Validador do pedido.</param>
public sealed class EmailService(IEmailFilaRepository emailFilaRepository, IValidator<NovoEmail> validator) : IEmailService
{
    /// <inheritdoc />
    /// <remarks>
    /// Não chama <c>SalvarAsync</c>: o e-mail entra na **mesma transação** de quem o originou.
    /// Se o cadastro falhar depois de enfileirar as boas-vindas, o e-mail desfaz junto — em vez
    /// de o usuário receber boas-vindas de uma conta que não existe.
    /// </remarks>
    public async Task<Result<Guid>> Enfileirar(NovoEmail dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<Guid>(validacao.Erros);

        var email = new EmailNaFila
        {
            Para = dados.Para.Trim(),
            Assunto = dados.Assunto.Trim(),
            CorpoHtml = dados.CorpoHtml,
            Prioridade = dados.Prioridade,
        };

        await emailFilaRepository.Adicionar(email, ct);

        return Result.Ok(email.Id);
    }
}
