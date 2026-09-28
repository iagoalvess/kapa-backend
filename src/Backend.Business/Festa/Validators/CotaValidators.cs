using Backend.Business.Festa.Models;
using FluentValidation;

namespace Backend.Business.Festa.Validators;

/// <summary>Forma da cota de convites de um evento (Sprint 30).</summary>
public sealed class DadosDaCotaValidator : AbstractValidator<DadosDaCota>
{
    /// <summary>Teto por formando: dez já é generoso num auditório; dedo trocado não vira 100.</summary>
    public const int CotaMaxima = 10;

    /// <summary>Teto de lugares: estádio, não auditório.</summary>
    public const int CapacidadeMaxima = 100_000;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDaCotaValidator()
    {
        RuleFor(x => x.CotaPorFormando).InclusiveBetween(1, CotaMaxima).WithMessage($"Informe de 1 a {CotaMaxima} convites por formando.");

        RuleFor(x => x.Capacidade).InclusiveBetween(1, CapacidadeMaxima).WithMessage("Informe de 1 a 100.000 lugares.");
    }
}
