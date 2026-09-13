using Backend.Business.Formaturas.Models;
using FluentValidation;

namespace Backend.Business.Formaturas.Validators;

/// <summary>
/// Valida a forma dos dados cadastrais, na criação e na edição.
/// </summary>
/// <remarks>
/// Um validator só porque o payload é um só: criar e editar mexem nos mesmos campos.
/// <para>
/// O ano corrente é lido <b>a cada validação</b>, dentro do <c>Must</c>: o validator é singleton,
/// e um <c>InclusiveBetween</c> congelaria o ano em que o processo subiu.
/// </para>
/// </remarks>
public sealed class DadosDaFormaturaValidator : AbstractValidator<DadosDaFormatura>
{
    /// <summary>Anos à frente aceitos para a conclusão.</summary>
    public const int AnosAFrente = 8;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDaFormaturaValidator()
    {
        RuleFor(x => x.Nome).Must(nome => nome?.Trim().Length is >= 3 and <= 120).WithMessage("O nome deve ter entre 3 e 120 caracteres.");

        RuleFor(x => x.Instituicao).NotEmpty().WithMessage("A instituição é obrigatória.").MaximumLength(120);

        RuleFor(x => x.Curso).NotEmpty().WithMessage("O curso é obrigatório.").MaximumLength(120);

        RuleFor(x => x.Semestre).InclusiveBetween(1, 2).WithMessage("O semestre deve ser 1 ou 2.");

        RuleFor(x => x.Ano)
            .Must(ano => ano >= DateTime.UtcNow.Year && ano <= DateTime.UtcNow.Year + AnosAFrente)
            .WithMessage($"O ano deve estar entre o ano corrente e os próximos {AnosAFrente} anos.");

        RuleFor(x => x.QuantidadeEstimadaDeFormandos)
            .InclusiveBetween(1, 2000)
            .WithMessage("A quantidade estimada de formandos deve estar entre 1 e 2000.");
    }
}
