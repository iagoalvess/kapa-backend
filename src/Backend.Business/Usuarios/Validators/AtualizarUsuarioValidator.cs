using Backend.Business.Usuarios.Models;
using FluentValidation;

namespace Backend.Business.Usuarios.Validators;

/// <summary>
/// Valida a forma dos dados de alteração de usuário.
/// </summary>
/// <remarks>
/// Validator cuida de **forma** (obrigatório, tamanho, formato). Regra que depende do estado do
/// sistema — "este e-mail já existe", "este usuário é o último administrador" — é do service,
/// que tem acesso ao repositório. Validator que consulta banco vira consulta escondida.
/// </remarks>
public sealed class AtualizarUsuarioValidator : AbstractValidator<AtualizarUsuario>
{
    /// <summary>Registra as regras de validação.</summary>
    public AtualizarUsuarioValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty()
            .WithMessage("O nome é obrigatório.")
            .MaximumLength(120)
            .WithMessage("O nome deve ter no máximo 120 caracteres.");
    }
}
