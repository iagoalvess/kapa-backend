using Backend.Business.Auth.Models;
using Backend.Business.Common.Validacao;
using Backend.Business.Legal.Models;
using Backend.Business.Legal.Validators;
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
        RuleFor(x => x.Email).EmailObrigatorio();

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
        RuleFor(x => x.Nome).NomeDaPessoa();

        RuleFor(x => x.Email).EmailObrigatorio().MaximumLength(256).WithMessage("O e-mail deve ter no máximo 256 caracteres.");

        RuleFor(x => x.Senha).NotEmpty().WithMessage("A senha é obrigatória.");

        RuleFor(x => x.Aceites)
            .Must(aceites => aceites is not null && TipoDeDocumento.Todos.All(tipo => aceites.Any(a => TipoDeDocumento.Normalizar(a.Tipo) == tipo)))
            .WithErrorCode(RegistrarAceiteValidator.AceiteObrigatorio)
            .WithMessage("É preciso aceitar os Termos de Uso e a Política de Privacidade para criar a conta.");
    }
}
