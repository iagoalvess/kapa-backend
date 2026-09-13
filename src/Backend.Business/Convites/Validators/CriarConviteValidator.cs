using Backend.Business.Convites.Models;
using Backend.Business.Formaturas.Models;
using FluentValidation;

namespace Backend.Business.Convites.Validators;

/// <summary>
/// Forma do pedido de convite.
/// </summary>
/// <remarks>
/// Quem pode oferecer papel de comissão depende do vínculo do autor e é do service.
/// <para>
/// Link da turma (sem e-mail) só oferece <c>Formando</c>: ele circula em grupo de WhatsApp, e um
/// link de Tesoureiro vazado é a tesouraria entregue a quem o pegar primeiro.
/// </para>
/// </remarks>
public sealed class CriarConviteValidator : AbstractValidator<CriarConvite>
{
    /// <summary>Validade máxima, em dias — o link da turma dura no máximo um semestre.</summary>
    public const int MaximoDeDias = 180;

    /// <summary>Registra as regras de validação.</summary>
    public CriarConviteValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Informe o e-mail.")
            .EmailAddress()
            .WithMessage("E-mail inválido.")
            .MaximumLength(256)
            .When(x => x.Email is not null);

        RuleFor(x => x.Papel)
            .Must(papel => PapelNaFormatura.Todos.Contains(papel, StringComparer.Ordinal))
            .WithErrorCode("convite.papel_invalido")
            .WithMessage("Papel inválido. Use Presidente, Tesoureiro, Comissao ou Formando.")
            .When(x => x.Papel is not null);

        RuleFor(x => x.Papel)
            .Equal(PapelNaFormatura.Formando)
            .WithErrorCode("convite.link_so_para_formando")
            .WithMessage("O link da turma só convida formandos. Para a comissão, envie o convite por e-mail.")
            .When(x => x.Email is null && x.Papel is not null && PapelNaFormatura.Todos.Contains(x.Papel, StringComparer.Ordinal));

        RuleFor(x => x.DiasDeValidade)
            .InclusiveBetween(1, MaximoDeDias)
            .WithMessage($"A validade vai de 1 a {MaximoDeDias} dias.")
            .When(x => x.DiasDeValidade is not null);

        RuleFor(x => x.UsosMaximos)
            .GreaterThan(0)
            .WithMessage("O limite de entradas precisa ser maior que zero.")
            .When(x => x.UsosMaximos is not null);
    }
}
