using Backend.Business.Cobrancas.Models;
using FluentValidation;

namespace Backend.Business.Cobrancas.Validators;

/// <summary>Forma do nome e das regras de atraso do plano.</summary>
/// <remarks>
/// Percentuais de 0 a 100% (base 10.000), sem teto de mercado: a decisão de 14/09/2026 foi deixar
/// livre e só avisar na tela acima de 2% de multa ou 1% de juros ao mês.
/// </remarks>
public sealed class DadosDoPlanoValidator : AbstractValidator<DadosDoPlano>
{
    /// <summary>100%, em base 10.000.</summary>
    public const int PercentualMaximo = 10_000;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDoPlanoValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty()
            .WithMessage("Dê um nome ao plano.")
            .MaximumLength(120)
            .WithMessage("O nome deve ter no máximo 120 caracteres.");

        RuleFor(x => x.PercentualDeMulta).InclusiveBetween(0, PercentualMaximo).WithMessage("A multa deve ficar entre 0% e 100%.");
        RuleFor(x => x.PercentualDeJurosAoMes).InclusiveBetween(0, PercentualMaximo).WithMessage("Os juros devem ficar entre 0% e 100% ao mês.");
        RuleFor(x => x.CarenciaEmDias).InclusiveBetween(0, 60).WithMessage("A carência deve ficar entre 0 e 60 dias.");
        RuleFor(x => x.PercentualDeDescontoPorAntecipacao)
            .InclusiveBetween(0, PercentualMaximo)
            .WithMessage("O desconto deve ficar entre 0% e 100%.");

        RuleFor(x => x.DiasMinimosParaDesconto).InclusiveBetween(0, 365).WithMessage("A antecedência do desconto deve ficar entre 0 e 365 dias.");

        RuleFor(x => x.DiasMinimosParaDesconto)
            .GreaterThan(0)
            .When(x => x.PercentualDeDescontoPorAntecipacao > 0)
            .WithErrorCode("cobranca.antecedencia_obrigatoria")
            .WithMessage("Diga com quantos dias de antecedência o desconto vale — senão ele sai para quem pagar um dia antes.");
    }
}

/// <summary>Forma de um item do plano.</summary>
/// <remarks>
/// O dia de vencimento vai de 1 a 31 (decisão de 14/09/2026); o mês que não tem o dia usa o último
/// (<see cref="GradeDeParcelas.Vencimento"/>).
/// </remarks>
public sealed class DadosDoItemValidator : AbstractValidator<DadosDoItem>
{
    /// <summary>Teto de parcelas: seis anos de mensalidade, a turma de medicina.</summary>
    public const int ParcelasMaximas = 120;

    /// <summary>Teto do valor de um item, em centavos: R$ 1.000.000,00.</summary>
    public const long ValorMaximo = 100_000_000;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDoItemValidator()
    {
        RuleFor(x => x.Tipo).IsInEnum().WithMessage("Tipo de cobrança inválido.");

        RuleFor(x => x.Descricao).MaximumLength(120).WithMessage("A descrição deve ter no máximo 120 caracteres.");

        RuleFor(x => x.ValorEmCentavos)
            .GreaterThan(0)
            .When(x => x.Tipo != TipoDeCobranca.Avulsa)
            .WithErrorCode("cobranca.valor_invalido")
            .WithMessage("Informe um valor maior que zero.");

        RuleFor(x => x.ValorEmCentavos)
            .NotEqual(0)
            .When(x => x.Tipo == TipoDeCobranca.Avulsa)
            .WithErrorCode("cobranca.valor_invalido")
            .WithMessage("Informe um valor diferente de zero.");

        RuleFor(x => x.ValorEmCentavos)
            .InclusiveBetween(-ValorMaximo, ValorMaximo)
            .WithErrorCode("cobranca.valor_invalido")
            .WithMessage("O valor deve ser de no máximo R$ 1.000.000,00.");

        RuleFor(x => x.NumeroDeParcelas).InclusiveBetween(1, ParcelasMaximas).WithMessage($"O número de parcelas vai de 1 a {ParcelasMaximas}.");

        RuleFor(x => x.DiaDeVencimento)
            .InclusiveBetween(1, 31)
            .WithErrorCode("cobranca.dia_invalido")
            .WithMessage("O vencimento vai do dia 1 ao 31.");

        RuleFor(x => x.PrimeiroMes.Year)
            .InclusiveBetween(2000, 2100)
            .OverridePropertyName("primeiro_mes")
            .WithMessage("Informe o mês do primeiro vencimento.");
    }
}

/// <summary>
/// Forma do rateio extraordinário: sem a origem da decisão, ele não existe.
/// </summary>
/// <remarks>
/// A origem é obrigatória porque é a única prova da cobrança — quem já aderiu passa a dever por
/// um item que o termo aceito não cita, e "quem mandou" não pode ficar na memória da tesouraria.
/// </remarks>
public sealed class RateioExtraordinarioValidator : AbstractValidator<RateioExtraordinario>
{
    /// <summary>Tamanho da origem da decisão.</summary>
    public const int TamanhoDaOrigem = 200;

    /// <summary>Registra as regras de validação.</summary>
    public RateioExtraordinarioValidator()
    {
        RuleFor(x => x.OrigemDaDecisao)
            .NotEmpty()
            .WithErrorCode("cobranca.origem_obrigatoria")
            .WithMessage("Informe onde a turma decidiu esta cobrança — a assembleia e a data.")
            .MaximumLength(TamanhoDaOrigem)
            .WithMessage($"A origem deve ter no máximo {TamanhoDaOrigem} caracteres.");
    }
}

/// <summary>Forma do pedido de simulação: cada item como no cadastro.</summary>
public sealed class SimularPlanoValidator : AbstractValidator<SimularPlano>
{
    /// <summary>Teto de itens simulados de uma vez.</summary>
    public const int ItensMaximos = 20;

    /// <summary>Registra as regras de validação.</summary>
    public SimularPlanoValidator()
    {
        When(
            x => x.Itens is not null,
            () =>
            {
                RuleFor(x => x.Itens!.Count)
                    .LessThanOrEqualTo(ItensMaximos)
                    .OverridePropertyName("itens")
                    .WithMessage($"Simule até {ItensMaximos} itens.");
                RuleForEach(x => x.Itens).SetValidator(new DadosDoItemValidator());
            }
        );
    }
}
