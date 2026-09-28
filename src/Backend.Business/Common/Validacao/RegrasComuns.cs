using Backend.Business.Common.Datas;
using FluentValidation;

namespace Backend.Business.Common.Validacao;

/// <summary>
/// Regras de forma que mais de uma feature aplica — a mesma pergunta, com a mesma mensagem.
/// </summary>
public static class RegrasComuns
{
    /// <summary>Tamanho máximo do nome de uma pessoa.</summary>
    public const int NomeMaximo = 120;

    /// <summary>Quantos anos para trás e para frente uma data da turma pode cair.</summary>
    /// <remarks>Dedo trocado em 2026 → 2062 faz a projeção desenhar 36 anos de meses vazios.</remarks>
    public const int AnosDeFolga = 10;

    /// <summary>Nome de pessoa: obrigatório e até <see cref="NomeMaximo"/> caracteres.</summary>
    public static IRuleBuilderOptions<T, string> NomeDaPessoa<T>(this IRuleBuilder<T, string> regra) =>
        regra
            .NotEmpty()
            .WithMessage("O nome é obrigatório.")
            .MaximumLength(NomeMaximo)
            .WithMessage($"O nome deve ter no máximo {NomeMaximo} caracteres.");

    /// <summary>Data dentro da janela em que uma turma existe.</summary>
    /// <param name="regra">A regra do campo.</param>
    /// <param name="campo">Como o campo aparece na mensagem, com artigo: "o vencimento".</param>
    public static IRuleBuilderOptions<T, DateOnly> DataPlausivel<T>(this IRuleBuilderInitial<T, DateOnly> regra, string campo) =>
        regra
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage($"Informe {campo}.")
            .Must(data => data.Year >= DataUtils.Hoje().Year - AnosDeFolga && data.Year <= DataUtils.Hoje().Year + AnosDeFolga)
            .WithMessage($"Confira {campo}: o ano está fora do período da turma.");
}
