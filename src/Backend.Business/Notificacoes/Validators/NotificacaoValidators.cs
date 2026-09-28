using Backend.Business.Notificacoes.Models;
using FluentValidation;

namespace Backend.Business.Notificacoes.Validators;

/// <summary>Forma das preferências do titular.</summary>
public sealed class DadosDasPreferenciasValidator : AbstractValidator<DadosDasPreferencias>
{
    /// <summary>Registra as regras de validação.</summary>
    public DadosDasPreferenciasValidator()
    {
        RuleFor(x => x.Preferencias).NotNull().WithMessage("Informe as preferências.");

        RuleForEach(x => x.Preferencias).ChildRules(item => item.RuleFor(p => p.Tipo).IsInEnum().WithMessage("Tipo de notificação desconhecido."));
    }
}
