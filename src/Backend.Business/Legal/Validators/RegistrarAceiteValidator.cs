using Backend.Business.Legal.Models;
using FluentValidation;

namespace Backend.Business.Legal.Validators;

/// <summary>
/// Valida a forma de um pedido de aceite.
/// </summary>
/// <remarks>
/// Se a versão existe e se é a vigente depende do banco, e fica no service.
/// </remarks>
public sealed class RegistrarAceiteValidator : AbstractValidator<RegistrarAceites>
{
    /// <summary>Código devolvido quando não há aceite no pedido.</summary>
    public const string AceiteObrigatorio = "legal.aceite_obrigatorio";

    /// <summary>Registra as regras de validação.</summary>
    public RegistrarAceiteValidator()
    {
        RuleFor(x => x.Aceites).NotEmpty().WithErrorCode(AceiteObrigatorio).WithMessage("Informe ao menos um documento aceito.");

        RuleForEach(x => x.Aceites)
            .ChildRules(aceite =>
            {
                aceite.RuleFor(a => a.Tipo).NotEmpty().WithMessage("Informe o documento aceito.");
                aceite.RuleFor(a => a.Versao).NotEmpty().WithMessage("Informe a versão aceita.").MaximumLength(40);
            });
    }
}
