using Backend.Business.Common.Datas;
using Backend.Business.MercadoPago.Models;
using Backend.Business.Pagamentos.Models;
using FluentValidation;

namespace Backend.Business.Pagamentos.Validators;

/// <summary>Os limites comuns a quem informa e a quem baixa.</summary>
internal static class LimitesDoPagamento
{
    /// <summary>Teto de um pagamento, em centavos: R$ 1.000.000,00 — o mesmo teto do item do plano.</summary>
    public const long ValorMaximo = 100_000_000;

    /// <summary>Tamanho do motivo da recusa e da justificativa do estorno.</summary>
    public const int TextoMaximo = 500;

    /// <summary>Dia do pagamento: informado, e não no futuro — o fuso é o de quem paga.</summary>
    public static IRuleBuilderOptions<T, DateOnly> DiaDoPagamento<T>(this IRuleBuilderInitial<T, DateOnly> regra) =>
        regra
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Informe a data do pagamento.")
            .Must(dia => dia <= DataUtils.Hoje())
            .WithMessage("A data do pagamento não pode estar no futuro.");

    /// <summary>Valor positivo e abaixo do teto.</summary>
    public static IRuleBuilderOptions<T, long> ValorDoPagamento<T>(this IRuleBuilder<T, long> regra) =>
        regra
            .GreaterThan(0)
            .WithMessage("Informe um valor maior que zero.")
            .LessThanOrEqualTo(ValorMaximo)
            .WithMessage("O valor deve ser de no máximo R$ 1.000.000,00.");
}

/// <summary>
/// Forma do "já paguei".
/// </summary>
/// <remarks>
/// O meio não é conferido contra os que a turma habilitou (P7 de 21/09/2026): o formando pode ter
/// pago de um jeito que ninguém previu, e recusar o aviso não desfaz o pagamento — só o esconde da
/// tesouraria. O comprovante segue opcional em todo meio (P6).
/// </remarks>
public sealed class NovoInformeValidator : AbstractValidator<NovoInforme>
{
    /// <summary>Registra as regras de validação.</summary>
    public NovoInformeValidator()
    {
        RuleFor(x => x.PagoEm).DiaDoPagamento();
        RuleFor(x => x.ValorEmCentavos).ValorDoPagamento();
        RuleFor(x => x.Meio).IsInEnum().WithMessage("Meio de pagamento inválido. Use Pix, Transferencia, Dinheiro ou Outro.");
    }
}

/// <summary>Forma da baixa manual.</summary>
/// <remarks>
/// <see cref="FormaDePagamento.Cartao"/> é só da baixa automática (Sprint 42, F10): o cartão passa pelo Mercado Pago da
/// turma, e a tesouraria que registra "cartão" à mão cria uma entrada que nenhum extrato do Mercado Pago confirma.
/// </remarks>
public sealed class BaixaManualValidator : AbstractValidator<BaixaManual>
{
    /// <summary>Registra as regras de validação.</summary>
    public BaixaManualValidator()
    {
        RuleFor(x => x.Forma)
            .Must(forma => Enum.IsDefined(forma) && forma != FormaDePagamento.Cartao)
            .WithMessage("Forma de pagamento inválida. Use Pix, Dinheiro, Transferencia ou Outro.");
        RuleFor(x => x.PagoEm).DiaDoPagamento();
        RuleFor(x => x.ValorEmCentavos).ValorDoPagamento();
    }
}

/// <summary>Forma do lote da conferência.</summary>
/// <remarks>Teto de 200 por lote: oitenta formandos por mês cabem com folga, e o lote é uma transação só.</remarks>
public sealed class ConfirmarInformesValidator : AbstractValidator<ConfirmarInformes>
{
    /// <summary>Informes por lote.</summary>
    public const int TamanhoMaximo = 200;

    /// <summary>Registra as regras de validação.</summary>
    public ConfirmarInformesValidator()
    {
        RuleFor(x => x.Itens)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Marque ao menos um pagamento para confirmar.")
            .Must(itens => itens.Count <= TamanhoMaximo)
            .WithMessage($"Confirme no máximo {TamanhoMaximo} pagamentos de uma vez.")
            .Must(itens => itens.Select(i => i.InformeId).Distinct().Count() == itens.Count)
            .WithMessage("O mesmo pagamento aparece duas vezes no lote.");

        RuleForEach(x => x.Itens).ChildRules(item => item.RuleFor(i => i.ValorRecebidoEmCentavos).ValorDoPagamento());
    }
}

/// <summary>Forma da recusa: o motivo é obrigatório, porque vai ao formando.</summary>
public sealed class RecusarInformeValidator : AbstractValidator<RecusarInforme>
{
    /// <summary>Registra as regras de validação.</summary>
    public RecusarInformeValidator() =>
        RuleFor(x => x.Motivo)
            .Must(motivo => !string.IsNullOrWhiteSpace(motivo))
            .WithMessage("Diga ao formando por que o pagamento foi recusado.")
            .MaximumLength(LimitesDoPagamento.TextoMaximo)
            .WithMessage($"O motivo deve ter no máximo {LimitesDoPagamento.TextoMaximo} caracteres.");
}

/// <summary>Forma do estorno: a justificativa é obrigatória, porque fica na auditoria.</summary>
public sealed class EstornarBaixaValidator : AbstractValidator<EstornarBaixa>
{
    /// <summary>Registra as regras de validação.</summary>
    public EstornarBaixaValidator() =>
        RuleFor(x => x.Justificativa)
            .Must(justificativa => !string.IsNullOrWhiteSpace(justificativa))
            .WithMessage("Explique por que a baixa está sendo desfeita.")
            .MaximumLength(LimitesDoPagamento.TextoMaximo)
            .WithMessage($"A justificativa deve ter no máximo {LimitesDoPagamento.TextoMaximo} caracteres.");
}

/// <summary>Forma do cancelamento avulso: a justificativa é obrigatória, porque fica na auditoria.</summary>
public sealed class CancelarParcelaValidator : AbstractValidator<CancelarParcela>
{
    /// <summary>Registra as regras de validação.</summary>
    public CancelarParcelaValidator() =>
        RuleFor(x => x.Justificativa)
            .Must(justificativa => !string.IsNullOrWhiteSpace(justificativa))
            .WithMessage("Explique por que a parcela está sendo cancelada.")
            .MaximumLength(LimitesDoPagamento.TextoMaximo)
            .WithMessage($"A justificativa deve ter no máximo {LimitesDoPagamento.TextoMaximo} caracteres.");
}

/// <summary>Forma do fechamento do pago sem parcela: o que a comissão fez fica registrado.</summary>
public sealed class FecharValorADevolverValidator : AbstractValidator<FecharValorADevolver>
{
    /// <summary>Registra as regras de validação.</summary>
    public FecharValorADevolverValidator() =>
        RuleFor(x => x.Observacao)
            .Must(observacao => !string.IsNullOrWhiteSpace(observacao))
            .WithMessage("Diga o que foi feito com este pagamento.")
            .MaximumLength(LimitesDoPagamento.TextoMaximo)
            .WithMessage($"A observação deve ter no máximo {LimitesDoPagamento.TextoMaximo} caracteres.");
}

/// <summary>
/// Forma do cartão que o formulário do Mercado Pago tokenizou no navegador (Sprint 39) — na parcela e na loja.
/// </summary>
/// <remarks>Só a forma: se o token vale e se o cartão passa, quem diz é o Mercado Pago na cobrança.</remarks>
public sealed class CartaoTokenizadoValidator : AbstractValidator<CartaoTokenizado>
{
    /// <summary>Monta as regras.</summary>
    public CartaoTokenizadoValidator()
    {
        RuleFor(c => c.Token).NotEmpty().WithMessage("O cartão não foi lido. Preencha os dados do cartão de novo.").MaximumLength(200);
        RuleFor(c => c.Bandeira).NotEmpty().WithMessage("O cartão não foi lido. Preencha os dados do cartão de novo.").MaximumLength(40);
        RuleFor(c => c.Parcelas)
            .InclusiveBetween(1, MeiosDePagamento.ParcelasNoCartao)
            .WithMessage($"Escolha de 1 a {MeiosDePagamento.ParcelasNoCartao} vezes.");
    }
}
