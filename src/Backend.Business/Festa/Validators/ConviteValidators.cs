using Backend.Business.Common.Texto;
using Backend.Business.Common.Validacao;
using Backend.Business.Festa.Models;
using FluentValidation;

namespace Backend.Business.Festa.Validators;

/// <summary>
/// Forma do titular de um convite: nome, documento e e-mail (P5.1).
/// </summary>
/// <remarks>
/// Documento é opcional enquanto a lista está aberta — o convite pode ser "a definir" e depois
/// "Maria" sem documento ainda —, mas tipo e número andam juntos, e o número informado precisa
/// existir: CPF pelo dígito verificador, RG pela forma, porque cada estado numera de um jeito. Quem
/// exige o documento no fechamento é o service, que sabe a hora da festa.
/// </remarks>
public sealed class DadosDoConvidadoValidator : AbstractValidator<DadosDoConvidado>
{
    /// <summary>Tamanho mínimo do RG, sem pontuação.</summary>
    public const int RgMinimo = 5;

    /// <summary>Tamanho máximo do RG, sem pontuação — o maior formato estadual, com folga.</summary>
    public const int RgMaximo = 14;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDoConvidadoValidator()
    {
        RuleFor(x => x.Nome).NomeDaPessoa();

        RuleFor(x => x.TipoDoDocumento)
            .NotNull()
            .When(x => !string.IsNullOrWhiteSpace(x.NumeroDoDocumento))
            .WithMessage("Diga se o documento é CPF ou RG.")
            .IsInEnum()
            .WithMessage("Tipo de documento inválido.");

        RuleFor(x => x.NumeroDoDocumento).NotEmpty().When(x => x.TipoDoDocumento is not null).WithMessage("Informe o número do documento.");

        RuleFor(x => x.NumeroDoDocumento)
            .Must(FormatosBrasileiros.CpfValido)
            .When(x => x.TipoDoDocumento is TipoDeDocumento.Cpf && !string.IsNullOrWhiteSpace(x.NumeroDoDocumento))
            .WithMessage("CPF inválido. Confira os números.");

        RuleFor(x => x.NumeroDoDocumento)
            .Must(numero => DocumentoDoConvidado.Normalizar(TipoDeDocumento.Rg, numero)?.Length is >= RgMinimo and <= RgMaximo)
            .When(x => x.TipoDoDocumento is TipoDeDocumento.Rg && !string.IsNullOrWhiteSpace(x.NumeroDoDocumento))
            .WithMessage($"RG inválido: de {RgMinimo} a {RgMaximo} letras e números.");

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("E-mail inválido.")
            .MaximumLength(254)
            .WithMessage("E-mail longo demais.");
    }
}

/// <summary>Forma da cortesia: titular com nome, e motivo — é uma cadeira dada, e precisa de autor e razão.</summary>
public sealed class DadosDaCortesiaValidator : AbstractValidator<DadosDaCortesia>
{
    /// <summary>Tamanho máximo do motivo.</summary>
    public const int MotivoMaximo = 300;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDaCortesiaValidator()
    {
        RuleFor(x => x.Convidado).SetValidator(new DadosDoConvidadoValidator());

        RuleFor(x => x.Motivo)
            .NotEmpty()
            .WithMessage("Diga por que a turma está dando este convite.")
            .MaximumLength(MotivoMaximo)
            .WithMessage($"O motivo deve ter no máximo {MotivoMaximo} caracteres.");
    }
}

/// <summary>Forma da liberação manual: o pedido e o motivo, que vai para a auditoria (P2).</summary>
public sealed class LiberacaoDeConvitesValidator : AbstractValidator<LiberacaoDeConvites>
{
    /// <summary>Registra as regras de validação.</summary>
    public LiberacaoDeConvitesValidator()
    {
        RuleFor(x => x.PedidoId).NotEmpty().WithMessage("Informe o pedido.");

        RuleFor(x => x.Motivo)
            .NotEmpty()
            .WithMessage("Diga por que o convite sai antes da quitação.")
            .MaximumLength(DadosDaCortesiaValidator.MotivoMaximo)
            .WithMessage($"O motivo deve ter no máximo {DadosDaCortesiaValidator.MotivoMaximo} caracteres.");
    }
}
