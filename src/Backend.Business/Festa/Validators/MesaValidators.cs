using Backend.Business.Festa.Models;
using FluentValidation;

namespace Backend.Business.Festa.Validators;

/// <summary>Forma do cadastro de uma mesa.</summary>
public sealed class DadosDaMesaValidator : AbstractValidator<DadosDaMesa>
{
    /// <summary>Tamanho da identificação.</summary>
    public const int IdentificacaoMaxima = 60;

    /// <summary>Tamanho da observação.</summary>
    public const int ObservacaoMaxima = 200;

    /// <summary>Teto de lugares: mesa de 40 já é banquete; dedo trocado não vira mesa de 400.</summary>
    public const int LugaresMaximo = 40;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDaMesaValidator()
    {
        RuleFor(x => x.Identificacao)
            .NotEmpty()
            .WithMessage("Informe como a mesa se chama.")
            .MaximumLength(IdentificacaoMaxima)
            .WithMessage($"A identificação deve ter no máximo {IdentificacaoMaxima} caracteres.");

        RuleFor(x => x.Lugares).InclusiveBetween(1, LugaresMaximo).WithMessage($"Informe de 1 a {LugaresMaximo} lugares.");

        RuleFor(x => x.Observacao).MaximumLength(ObservacaoMaxima).WithMessage($"A observação deve ter no máximo {ObservacaoMaxima} caracteres.");
    }
}
