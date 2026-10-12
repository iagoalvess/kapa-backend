using Backend.Business.Cobrancas.Models;
using FluentValidation;

namespace Backend.Business.Cobrancas.Validators;

/// <summary>Forma da condição financeira (Sprint 54): os benefícios.</summary>
/// <remarks>Se cada pacote existe no catálogo, e se dois benefícios caem no mesmo grupo de faixas, quem confere é o service.</remarks>
public sealed class DadosDaCondicaoValidator : AbstractValidator<DadosDaCondicao>
{
    /// <summary>Registra as regras de validação.</summary>
    public DadosDaCondicaoValidator()
    {
        RuleFor(x => x.Beneficios)
            .NotEmpty()
            .WithErrorCode("cobranca.condicao_sem_beneficio")
            .WithMessage("Escolha ao menos um pacote com desconto ou isenção.")
            .Must(beneficios => beneficios.Select(b => b.ItemDeCobrancaId).Distinct().Count() == beneficios.Count)
            .WithErrorCode("cobranca.condicao_pacote_repetido")
            .WithMessage("Cada pacote entra uma vez só.")
            .OverridePropertyName("beneficios");

        RuleForEach(x => x.Beneficios)
            .Must(b => (b.Percentual is null) != (b.ValorEmCentavos is null))
            .WithErrorCode("cobranca.condicao_forma_invalida")
            .WithMessage("Informe percentual ou valor fixo, um dos dois.")
            .Must(b => b.Percentual is null || b.Percentual is > 0 and <= BeneficioDaCondicao.Inteiro)
            .WithErrorCode("cobranca.condicao_percentual_invalido")
            .WithMessage("O percentual vai de 0,01% a 100%.")
            .Must(b => b.ValorEmCentavos is null || b.ValorEmCentavos > 0)
            .WithErrorCode("cobranca.condicao_valor_invalido")
            .WithMessage("O valor do desconto precisa ser maior que zero.")
            .OverridePropertyName("beneficios");
    }
}
