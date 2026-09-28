using Backend.Business.Formaturas.Models;
using FluentValidation;

namespace Backend.Business.Formaturas.Validators;

/// <summary>
/// Valida a forma do desligamento.
/// </summary>
/// <remarks>
/// "Tem adesão", "já está desligado" e "é o último presidente" dependem do estado da turma e ficam
/// no service.
/// </remarks>
public sealed class DesligarFormandoValidator : AbstractValidator<DesligarFormando>
{
    /// <summary>Tamanho máximo da justificativa — cabe o parágrafo, não a ata.</summary>
    public const int TamanhoDoDetalhe = 200;

    /// <summary>Registra as regras de validação.</summary>
    /// <remarks>
    /// O detalhe é obrigatório porque, dois anos depois, "por que o João saiu" é pergunta de
    /// assembleia — e "Outro", sozinho, é o silêncio de uma coluna vazia com outro nome.
    /// </remarks>
    public DesligarFormandoValidator()
    {
        RuleFor(x => x.Motivo)
            .Must(motivo => MotivoDeSaida.Todos.Contains(motivo, StringComparer.Ordinal))
            .WithErrorCode("membro.motivo_invalido")
            .WithMessage("Motivo inválido. Escolha um da lista.");

        RuleFor(x => x.Detalhe)
            .NotEmpty()
            .WithErrorCode("membro.detalhe_obrigatorio")
            .WithMessage("Diga qual foi o motivo.")
            .When(x => x.Motivo == MotivoDeSaida.Outro);

        RuleFor(x => x.Detalhe).MaximumLength(TamanhoDoDetalhe);
    }
}
