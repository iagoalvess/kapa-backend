using Backend.Business.Assinaturas.Models;
using FluentValidation;

namespace Backend.Business.Assinaturas.Validators;

/// <summary>Forma do pedido de troca de plano. Se o plano existe e cabe na turma é o service quem sabe.</summary>
public sealed class TrocaDePlanoValidator : AbstractValidator<TrocaDePlano>
{
    /// <summary>Configura as regras.</summary>
    public TrocaDePlanoValidator()
    {
        RuleFor(x => x.PlanoCodigo).NotEmpty().WithMessage("Escolha um plano.").MaximumLength(40);
    }
}
