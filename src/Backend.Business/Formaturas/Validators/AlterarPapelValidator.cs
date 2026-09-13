using Backend.Business.Formaturas.Models;
using FluentValidation;

namespace Backend.Business.Formaturas.Validators;

/// <summary>
/// Valida a forma da troca de papel.
/// </summary>
/// <remarks>
/// "É o último presidente" depende do estado da turma e fica no service.
/// </remarks>
public sealed class AlterarPapelValidator : AbstractValidator<AlterarPapel>
{
    /// <summary>Registra as regras de validação.</summary>
    public AlterarPapelValidator()
    {
        RuleFor(x => x.Papel)
            .Must(papel => PapelNaFormatura.Todos.Contains(papel, StringComparer.Ordinal))
            .WithErrorCode("membro.papel_invalido")
            .WithMessage("Papel inválido. Use Presidente, Tesoureiro, Comissao ou Formando.");
    }
}
