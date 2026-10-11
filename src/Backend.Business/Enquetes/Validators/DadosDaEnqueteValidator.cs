using Backend.Business.Enquetes.Models;
using FluentValidation;

namespace Backend.Business.Enquetes.Validators;

/// <summary>Forma da pergunta; regras que dependem do instante de publicação ficam no service.</summary>
public sealed class DadosDaEnqueteValidator : AbstractValidator<DadosDaEnquete>
{
    /// <summary>Registra limites e tipos da primeira versão.</summary>
    public DadosDaEnqueteValidator()
    {
        RuleFor(x => x.Pergunta).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Descricao).MaximumLength(2000);
        RuleFor(x => x.Tipo).IsInEnum();
        RuleFor(x => x.QuorumMinimoPercentual).InclusiveBetween(1, 100);
        RuleFor(x => x.EncerraEm).Must(x => x.Kind == DateTimeKind.Utc).WithMessage("Informe o prazo com fuso horário.");
        RuleFor(x => x.Opcoes).Must(x => x is { Count: >= 2 and <= 10 }).WithMessage("Informe de 2 a 10 opções.");
        RuleForEach(x => x.Opcoes).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Opcoes)
            .Must(x => x.Select(o => o?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == x.Count)
            .When(x => x.Opcoes is not null)
            .WithMessage("As opções devem ser diferentes.");
        RuleFor(x => x.MaximoDeEscolhas)
            .Must((dados, limite) => dados.Tipo == TipoDeEnquete.UnicaEscolha ? limite is null : limite is >= 1 && limite <= dados.Opcoes.Count)
            .When(x => x.Opcoes is not null)
            .WithMessage("Na múltipla escolha, informe um limite entre 1 e a quantidade de opções.");
    }
}
