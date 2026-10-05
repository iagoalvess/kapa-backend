using Backend.Business.Festa.Models;
using FluentValidation;

namespace Backend.Business.Festa.Validators;

/// <summary>Forma da capacidade do local de um evento.</summary>
public sealed class DadosDaCapacidadeValidator : AbstractValidator<DadosDaCapacidade>
{
    /// <summary>Teto de lugares: estádio, não auditório.</summary>
    public const int CapacidadeMaxima = 100_000;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDaCapacidadeValidator()
    {
        RuleFor(x => x.Capacidade).InclusiveBetween(1, CapacidadeMaxima).WithMessage("Informe de 1 a 100.000 lugares.");
    }
}
