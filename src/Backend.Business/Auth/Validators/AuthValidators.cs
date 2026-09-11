using Backend.Business.Auth.Models;
using FluentValidation;

namespace Backend.Business.Auth.Validators;

/// <summary>
/// Valida a forma das credenciais de login.
/// </summary>
public sealed class CredenciaisValidator : AbstractValidator<Credenciais>
{
    /// <summary>Registra as regras de validação.</summary>
    public CredenciaisValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("O e-mail é obrigatório.").EmailAddress().WithMessage("Informe um e-mail válido.");

        RuleFor(x => x.Senha).NotEmpty().WithMessage("A senha é obrigatória.");
    }
}

/// <summary>
/// Valida a forma dos dados de criação de conta.
/// </summary>
/// <remarks>
/// A **política de senha** (tamanho mínimo, dígito, maiúscula) não é repetida aqui de propósito:
/// ela vive uma única vez em <c>IdentityOptions</c>, e o <c>UserManager</c> a aplica. Duplicar a
/// regra em duas camadas garante que um dia as duas discordem.
/// </remarks>
public sealed class RegistrarUsuarioValidator : AbstractValidator<RegistrarUsuario>
{
    /// <summary>Registra as regras de validação.</summary>
    public RegistrarUsuarioValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty()
            .WithMessage("O nome é obrigatório.")
            .MaximumLength(120)
            .WithMessage("O nome deve ter no máximo 120 caracteres.");

        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("O e-mail é obrigatório.")
            .EmailAddress()
            .WithMessage("Informe um e-mail válido.")
            .MaximumLength(256)
            .WithMessage("O e-mail deve ter no máximo 256 caracteres.");

        RuleFor(x => x.Senha).NotEmpty().WithMessage("A senha é obrigatória.");
    }
}
