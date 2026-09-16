using Backend.Business.Adesoes.Models;
using FluentValidation;

namespace Backend.Business.Adesoes.Validators;

/// <summary>Forma do texto de uma versão nova do termo.</summary>
public sealed class PublicarTermoValidator : AbstractValidator<PublicarTermo>
{
    /// <summary>Teto do texto: dezenas de páginas de contrato cabem com folga.</summary>
    public const int TamanhoMaximo = 100_000;

    /// <summary>Registra as regras de validação.</summary>
    public PublicarTermoValidator()
    {
        RuleFor(x => x.Conteudo)
            .NotEmpty()
            .WithMessage("Escreva o texto do termo.")
            .MaximumLength(TamanhoMaximo)
            .WithMessage("O termo deve ter no máximo 100.000 caracteres.");
    }
}

/// <summary>
/// Forma do pedido de aceite: o hash de 64 dígitos hexadecimais que a tela recebeu e os seis dígitos
/// do código enviado por e-mail.
/// </summary>
/// <remarks>
/// Só a forma. Se o código <b>confere</b> é o service que diz, contra o provedor do Identity — aqui
/// ele para na porta se nem seis dígitos é, para não gastar a conferência com digitação incompleta.
/// </remarks>
public sealed class AderirAoTermoValidator : AbstractValidator<AderirAoTermo>
{
    /// <summary>Registra as regras de validação.</summary>
    public AderirAoTermoValidator()
    {
        RuleFor(x => x.HashDoConteudo)
            .NotEmpty()
            .WithMessage("Informe o hash do termo exibido.")
            .Matches("^[0-9a-f]{64}$")
            .WithMessage("O hash do termo é inválido.");

        RuleFor(x => x.Codigo)
            .NotEmpty()
            .WithMessage("Informe o código enviado para o seu e-mail.")
            .Matches("^[0-9]{6}$")
            .WithMessage("O código tem seis dígitos.");
    }
}
