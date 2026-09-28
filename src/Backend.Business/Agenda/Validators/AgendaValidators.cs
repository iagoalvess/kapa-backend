using Backend.Business.Agenda.Models;
using Backend.Business.Common.Validacao;
using FluentValidation;

namespace Backend.Business.Agenda.Validators;

/// <summary>
/// Forma de um evento da agenda.
/// </summary>
/// <remarks>
/// A data reusa o <c>DataPlausivel</c> do financeiro (em <c>RegrasComuns</c>), e não uma regra nova: é a mesma pergunta —
/// "esta data cabe na vida de uma turma?" — e o mesmo estrago quando não cabe (dedo trocado em 2026
/// → 2062 enche a agenda de meses vazios, como enchia a projeção do caixa).
/// <para>
/// Hora, local e descrição são opcionais: a maior parte dos eventos de uma formatura nasce com dia
/// e título, e só ganha o resto quando a comissão fecha.
/// </para>
/// </remarks>
public sealed class DadosDoEventoValidator : AbstractValidator<DadosDoEvento>
{
    /// <summary>Título do evento.</summary>
    public const int TituloMaximo = 120;

    /// <summary>Onde é.</summary>
    public const int LocalMaximo = 200;

    /// <summary>O que mais a turma precisa saber.</summary>
    public const int DescricaoMaximo = 1000;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDoEventoValidator()
    {
        RuleFor(x => x.Titulo)
            .NotEmpty()
            .WithMessage("Informe o que é o evento.")
            .MaximumLength(TituloMaximo)
            .WithMessage($"O título deve ter no máximo {TituloMaximo} caracteres.");

        RuleFor(x => x.Tipo).IsInEnum().WithMessage("Escolha o tipo do evento.");

        RuleFor(x => x.Situacao).IsInEnum().WithMessage("Escolha a situação do evento.");

        RuleFor(x => x.Data).DataPlausivel("a data do evento");

        RuleFor(x => x.Local).MaximumLength(LocalMaximo).WithMessage($"O local deve ter no máximo {LocalMaximo} caracteres.");

        RuleFor(x => x.Descricao).MaximumLength(DescricaoMaximo).WithMessage($"A descrição deve ter no máximo {DescricaoMaximo} caracteres.");
    }
}
