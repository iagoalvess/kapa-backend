using Backend.Business.Emails.Models;
using FluentValidation;

namespace Backend.Business.Emails.Validators;

/// <summary>
/// Valida a forma de um pedido de envio.
/// </summary>
/// <remarks>
/// A validação acontece no momento de enfileirar, e não na hora de enviar. Descobrir que o
/// endereço está malformado meia hora depois, num log do worker, é bem pior que recusar na
/// chamada que originou o e-mail.
/// </remarks>
public sealed class NovoEmailValidator : AbstractValidator<NovoEmail>
{
    /// <summary>Registra as regras de validação.</summary>
    public NovoEmailValidator()
    {
        RuleFor(x => x.Para)
            .NotEmpty()
            .WithMessage("O destinatário é obrigatório.")
            .EmailAddress()
            .WithMessage("O destinatário não é um e-mail válido.")
            .MaximumLength(256);

        RuleFor(x => x.Assunto).NotEmpty().WithMessage("O assunto é obrigatório.").MaximumLength(300);

        RuleFor(x => x.CorpoHtml).NotEmpty().WithMessage("O corpo da mensagem é obrigatório.");
    }
}
