using Backend.Business.Festa.Models;
using FluentValidation;

namespace Backend.Business.Festa.Validators;

/// <summary>
/// Forma de um item da festa.
/// </summary>
/// <remarks>
/// O valor é opcional — o item nasce "a contratar", muitas vezes antes de existir orçamento —, mas o
/// que for informado precisa caber no mundo: o teto é o mesmo da despesa da Sprint 10, porque o item
/// vira despesa e um teto mais frouxo aqui só adiaria a recusa para a outra tela.
/// <para>
/// A quantidade estimada só é cobrada no rateio por formando; no rateado pela turma ela é forçada a
/// 1 pela própria entidade, e validá-la ali obrigaria a tela a mandar um número que ela não pergunta.
/// </para>
/// </remarks>
public sealed class DadosDoItemDaFestaValidator : AbstractValidator<DadosDoItemDaFesta>
{
    /// <summary>Teto do valor previsto, em centavos: R$ 1.000.000,00, o mesmo da despesa.</summary>
    public const long ValorMaximo = 100_000_000;

    /// <summary>Título do item.</summary>
    public const int TituloMaximo = 120;

    /// <summary>O que vai ter, em Markdown.</summary>
    public const int OQueIncluiMaximo = 2000;

    /// <summary>Teto de formandos estimados: turma nenhuma passa disso, e dedo trocado infla o custo.</summary>
    public const int QuantidadeMaxima = 2000;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDoItemDaFestaValidator()
    {
        RuleFor(x => x.Titulo)
            .NotEmpty()
            .WithMessage("Informe o que é o item.")
            .MaximumLength(TituloMaximo)
            .WithMessage($"O título deve ter no máximo {TituloMaximo} caracteres.");

        RuleFor(x => x.Categoria).IsInEnum().WithMessage("Escolha a categoria do item.");

        RuleFor(x => x.Rateio).IsInEnum().WithMessage("Escolha quem paga o item.");

        RuleFor(x => x.OQueInclui).MaximumLength(OQueIncluiMaximo).WithMessage($"O que vai ter deve ter no máximo {OQueIncluiMaximo} caracteres.");

        RuleFor(x => x.ValorPrevistoEmCentavos)
            .GreaterThanOrEqualTo(0)
            .WithMessage("O valor não pode ser negativo.")
            .LessThanOrEqualTo(ValorMaximo)
            .WithMessage("O valor deve ser de no máximo R$ 1.000.000,00.");

        RuleFor(x => x.QuantidadeEstimada)
            .InclusiveBetween(1, QuantidadeMaxima)
            .WithMessage($"Informe de 1 a {QuantidadeMaxima} formandos.")
            .When(x => x.Rateio == TipoDeRateio.PorFormando);
    }
}

/// <summary>
/// Forma de uma proposta.
/// </summary>
/// <remarks>
/// O valor é opcional pelo mesmo motivo do item: a comissão levanta o nome da banda antes de ter o
/// preço dela. O teto é o mesmo do item, porque a proposta aceita vira a despesa.
/// </remarks>
public sealed class DadosDaPropostaValidator : AbstractValidator<DadosDaProposta>
{
    /// <summary>Título da proposta.</summary>
    public const int TituloMaximo = 120;

    /// <summary>O que ela entrega, em Markdown.</summary>
    public const int OQueIncluiMaximo = 1000;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDaPropostaValidator()
    {
        RuleFor(x => x.Titulo)
            .NotEmpty()
            .WithMessage("Informe quem está propondo.")
            .MaximumLength(TituloMaximo)
            .WithMessage($"O título deve ter no máximo {TituloMaximo} caracteres.");

        RuleFor(x => x.ValorEmCentavos)
            .GreaterThanOrEqualTo(0)
            .WithMessage("O valor não pode ser negativo.")
            .LessThanOrEqualTo(DadosDoItemDaFestaValidator.ValorMaximo)
            .WithMessage("O valor deve ser de no máximo R$ 1.000.000,00.");

        RuleFor(x => x.OQueInclui).MaximumLength(OQueIncluiMaximo).WithMessage($"O que inclui deve ter no máximo {OQueIncluiMaximo} caracteres.");
    }
}
