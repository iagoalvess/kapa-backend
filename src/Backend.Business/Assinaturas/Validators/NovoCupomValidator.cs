using Backend.Business.Assinaturas.Models;
using Backend.Business.Common.Datas;
using FluentValidation;

namespace Backend.Business.Assinaturas.Validators;

/// <summary>Forma do cupom novo. Se o código já existe é o service quem sabe.</summary>
public sealed class NovoCupomValidator : AbstractValidator<NovoCupom>
{
    /// <summary>Teto do limite de usos: cupom de campanha, não de catálogo.</summary>
    public const int LimiteDeUsosMaximo = 1000;

    /// <summary>Configura as regras.</summary>
    public NovoCupomValidator()
    {
        RuleFor(x => Cupom.Normalizar(x.Codigo))
            .Must(Cupom.FormaValida)
            .WithMessage($"Use de {Cupom.TamanhoMinimo} a {Cupom.TamanhoMaximo} letras, números ou hífen.")
            .OverridePropertyName("codigo");
        RuleFor(x => x.Percentual).InclusiveBetween(1, Cupom.PercentualMaximo).WithMessage($"O desconto vai de 1% a {Cupom.PercentualMaximo}%.");
        RuleFor(x => x.ValidoAte).Must(data => data >= DataUtils.Hoje()).WithMessage("A validade não pode ser no passado.");
        RuleFor(x => x.LimiteDeUsos).InclusiveBetween(1, LimiteDeUsosMaximo).WithMessage($"O limite de usos vai de 1 a {LimiteDeUsosMaximo}.");
    }
}
