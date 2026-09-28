using Backend.Business.Common.Datas;
using Backend.Business.Common.Texto;
using Backend.Business.Common.Validacao;
using Backend.Business.Financeiro.Models;
using FluentValidation;

namespace Backend.Business.Financeiro.Validators;

/// <summary>Os limites comuns ao cadastro de fornecedor e ao lançamento de despesa e de receita.</summary>
internal static class LimitesDoFinanceiro
{
    /// <summary>Teto de um lançamento, em centavos: R$ 1.000.000,00 — o mesmo teto do pagamento da Sprint 9.</summary>
    public const long ValorMaximo = 100_000_000;

    /// <summary>Parcelas de um lançamento. Dois anos de contrato cabem.</summary>
    public const int MaximoDeParcelas = 24;

    /// <summary>Descrição do lançamento, origem da receita e nome do fornecedor.</summary>
    public const int NomeMaximo = 200;

    /// <summary>Observações do fornecedor.</summary>
    public const int ObservacoesMaximo = 1000;

    /// <summary>Valor positivo e abaixo do teto.</summary>
    public static IRuleBuilderOptions<T, long> ValorDoLancamento<T>(this IRuleBuilder<T, long> regra) =>
        regra
            .GreaterThan(0)
            .WithMessage("Informe um valor maior que zero.")
            .LessThanOrEqualTo(ValorMaximo)
            .WithMessage("O valor deve ser de no máximo R$ 1.000.000,00.");
}

/// <summary>Forma do cadastro de fornecedor.</summary>
/// <remarks>
/// O documento é opcional — muitas turmas contratam quem só manda o PIX —, mas o que for informado
/// precisa ser um CNPJ ou um CPF de verdade: documento inválido no cadastro vira nota fiscal recusada
/// na prestação de contas.
/// </remarks>
public sealed class DadosDoFornecedorValidator : AbstractValidator<DadosDoFornecedor>
{
    /// <summary>Registra as regras de validação.</summary>
    public DadosDoFornecedorValidator()
    {
        RuleFor(x => x.Nome)
            .NotEmpty()
            .WithMessage("Informe o nome do fornecedor.")
            .MaximumLength(LimitesDoFinanceiro.NomeMaximo)
            .WithMessage($"O nome deve ter no máximo {LimitesDoFinanceiro.NomeMaximo} caracteres.");

        RuleFor(x => x.Categoria).IsInEnum().WithMessage("Escolha uma categoria da lista.");

        RuleFor(x => x.Documento)
            .Must(documento => FormatosBrasileiros.CnpjValido(documento) || FormatosBrasileiros.CpfValido(documento))
            .When(x => !string.IsNullOrWhiteSpace(x.Documento))
            .WithMessage("Informe um CNPJ ou CPF válido.");

        RuleFor(x => x.Telefone)
            .Must(telefone => FormatosBrasileiros.TelefoneE164(telefone) is not null)
            .When(x => !string.IsNullOrWhiteSpace(x.Telefone))
            .WithMessage("Informe um telefone com DDD.");

        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email)).WithMessage("Informe um e-mail válido.");

        RuleFor(x => x.Observacoes)
            .MaximumLength(LimitesDoFinanceiro.ObservacoesMaximo)
            .WithMessage($"As observações devem ter no máximo {LimitesDoFinanceiro.ObservacoesMaximo} caracteres.");
    }
}

/// <summary>Forma do lançamento de despesa.</summary>
public sealed class NovaDespesaValidator : AbstractValidator<NovaDespesa>
{
    /// <summary>Registra as regras de validação.</summary>
    public NovaDespesaValidator()
    {
        RuleFor(x => x.Descricao)
            .NotEmpty()
            .WithMessage("Diga o que é a despesa.")
            .MaximumLength(LimitesDoFinanceiro.NomeMaximo)
            .WithMessage($"A descrição deve ter no máximo {LimitesDoFinanceiro.NomeMaximo} caracteres.");

        RuleFor(x => x.Categoria).IsInEnum().WithMessage("Escolha uma categoria da lista.");
        RuleFor(x => x.ValorEmCentavos).ValorDoLancamento();

        RuleFor(x => x.NumeroDeParcelas)
            .InclusiveBetween(1, LimitesDoFinanceiro.MaximoDeParcelas)
            .WithMessage($"O número de parcelas deve ser de 1 a {LimitesDoFinanceiro.MaximoDeParcelas}.");

        RuleFor(x => x.Competencia).DataPlausivel("o mês de competência");
        RuleFor(x => x.Vencimento).DataPlausivel("o vencimento");

        RuleFor(x => x.PagaEm)
            .Must(dia => dia <= DataUtils.Hoje())
            .When(x => x.PagaEm is not null)
            .WithMessage("A data do pagamento não pode estar no futuro.");
    }
}

/// <summary>Forma da correção de uma despesa.</summary>
public sealed class DadosDaDespesaValidator : AbstractValidator<DadosDaDespesa>
{
    /// <summary>Registra as regras de validação.</summary>
    public DadosDaDespesaValidator()
    {
        RuleFor(x => x.Descricao)
            .NotEmpty()
            .WithMessage("Diga o que é a despesa.")
            .MaximumLength(LimitesDoFinanceiro.NomeMaximo)
            .WithMessage($"A descrição deve ter no máximo {LimitesDoFinanceiro.NomeMaximo} caracteres.");

        RuleFor(x => x.Categoria).IsInEnum().WithMessage("Escolha uma categoria da lista.");
        RuleFor(x => x.ValorEmCentavos).ValorDoLancamento();
        RuleFor(x => x.Competencia).DataPlausivel("o mês de competência");
        RuleFor(x => x.Vencimento).DataPlausivel("o vencimento");
    }
}

/// <summary>Forma do pagamento de uma despesa.</summary>
public sealed class PagarDespesaValidator : AbstractValidator<PagarDespesa>
{
    /// <summary>Registra as regras de validação.</summary>
    public PagarDespesaValidator() =>
        RuleFor(x => x.PagoEm)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Informe a data do pagamento.")
            .Must(dia => dia <= DataUtils.Hoje())
            .WithMessage("A data do pagamento não pode estar no futuro.");
}

/// <summary>Forma do lançamento de receita.</summary>
/// <remarks>
/// A descrição é obrigatória em toda categoria, e não só em <see cref="CategoriaDeOutraReceita.Outros"/>
/// (P3): é ela que nomeia a linha no extrato do caixa, e "Patrocínio" dez vezes seguidas não diz
/// qual patrocínio.
/// </remarks>
public sealed class NovaOutraReceitaValidator : AbstractValidator<NovaOutraReceita>
{
    /// <summary>Registra as regras de validação.</summary>
    public NovaOutraReceitaValidator()
    {
        RuleFor(x => x.Descricao)
            .NotEmpty()
            .WithMessage("Diga o que é a receita.")
            .MaximumLength(LimitesDoFinanceiro.NomeMaximo)
            .WithMessage($"A descrição deve ter no máximo {LimitesDoFinanceiro.NomeMaximo} caracteres.");

        RuleFor(x => x.Origem)
            .MaximumLength(LimitesDoFinanceiro.NomeMaximo)
            .WithMessage($"A origem deve ter no máximo {LimitesDoFinanceiro.NomeMaximo} caracteres.");

        RuleFor(x => x.Categoria).IsInEnum().WithMessage("Escolha uma categoria da lista.");
        RuleFor(x => x.ValorEmCentavos).ValorDoLancamento();
        RuleFor(x => x.Data).DataPlausivel("a data");

        RuleFor(x => x.Data).Must(dia => dia <= DataUtils.Hoje()).When(x => x.Recebida).WithMessage("Receita recebida não pode ter data no futuro.");
    }
}

/// <summary>Forma da correção de uma receita.</summary>
public sealed class DadosDaOutraReceitaValidator : AbstractValidator<DadosDaOutraReceita>
{
    /// <summary>Registra as regras de validação.</summary>
    public DadosDaOutraReceitaValidator()
    {
        RuleFor(x => x.Descricao)
            .NotEmpty()
            .WithMessage("Diga o que é a receita.")
            .MaximumLength(LimitesDoFinanceiro.NomeMaximo)
            .WithMessage($"A descrição deve ter no máximo {LimitesDoFinanceiro.NomeMaximo} caracteres.");

        RuleFor(x => x.Origem)
            .MaximumLength(LimitesDoFinanceiro.NomeMaximo)
            .WithMessage($"A origem deve ter no máximo {LimitesDoFinanceiro.NomeMaximo} caracteres.");

        RuleFor(x => x.Categoria).IsInEnum().WithMessage("Escolha uma categoria da lista.");
        RuleFor(x => x.ValorEmCentavos).ValorDoLancamento();
        RuleFor(x => x.Data).DataPlausivel("a data");
    }
}

/// <summary>Forma do recebimento de uma receita.</summary>
public sealed class ReceberOutraReceitaValidator : AbstractValidator<ReceberOutraReceita>
{
    /// <summary>Registra as regras de validação.</summary>
    public ReceberOutraReceitaValidator() =>
        RuleFor(x => x.RecebidaEm)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Informe o dia em que o dinheiro entrou.")
            .Must(dia => dia <= DataUtils.Hoje())
            .WithMessage("A data do recebimento não pode estar no futuro.");
}
