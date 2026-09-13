using Backend.Business.Assinaturas.Models;
using FluentValidation;

namespace Backend.Business.Assinaturas.Validators;

/// <summary>Forma do pedido de checkout. Se o plano existe é o service quem sabe.</summary>
public sealed class IniciarCheckoutValidator : AbstractValidator<IniciarCheckout>
{
    /// <summary>Configura as regras.</summary>
    public IniciarCheckoutValidator()
    {
        RuleFor(x => x.PlanoCodigo).NotEmpty().WithMessage("Escolha um plano.").MaximumLength(40);
    }
}
