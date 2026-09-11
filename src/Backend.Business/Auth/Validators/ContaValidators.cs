using Backend.Business.Auth.Models;
using FluentValidation;

namespace Backend.Business.Auth.Validators;

/// <summary>
/// Valida um pedido identificado apenas pelo e-mail.
/// </summary>
public sealed class PedidoPorEmailValidator : AbstractValidator<PedidoPorEmail>
{
    /// <summary>Registra as regras de validação.</summary>
    public PedidoPorEmailValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("O e-mail é obrigatório.").EmailAddress().WithMessage("Informe um e-mail válido.");
    }
}

/// <summary>
/// Valida o consumo do link de redefinição de senha.
/// </summary>
/// <remarks>
/// A força da nova senha não é conferida aqui: a política vive em <c>IdentityOptions</c> e o
/// <c>ResetPasswordAsync</c> a aplica. O validador só garante que os três campos chegaram.
/// </remarks>
public sealed class RedefinirSenhaValidator : AbstractValidator<RedefinirSenha>
{
    /// <summary>Registra as regras de validação.</summary>
    public RedefinirSenhaValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithMessage("O e-mail é obrigatório.").EmailAddress().WithMessage("Informe um e-mail válido.");

        RuleFor(x => x.Token).NotEmpty().WithMessage("O link de redefinição está incompleto.");

        RuleFor(x => x.NovaSenha).NotEmpty().WithMessage("A nova senha é obrigatória.");
    }
}

/// <summary>
/// Valida a troca de senha por um usuário autenticado.
/// </summary>
public sealed class AlterarSenhaValidator : AbstractValidator<AlterarSenha>
{
    /// <summary>Registra as regras de validação.</summary>
    public AlterarSenhaValidator()
    {
        RuleFor(x => x.SenhaAtual).NotEmpty().WithMessage("A senha atual é obrigatória.");

        RuleFor(x => x.NovaSenha)
            .NotEmpty()
            .WithMessage("A nova senha é obrigatória.")
            .NotEqual(x => x.SenhaAtual)
            .WithMessage("A nova senha deve ser diferente da atual.");
    }
}
