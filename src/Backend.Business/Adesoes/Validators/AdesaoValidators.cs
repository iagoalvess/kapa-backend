using Backend.Business.Adesoes.Models;
using Backend.Business.Cobrancas.Validators;
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
    /// <summary>Teto da cesta: mais que isso é catálogo de loja, não de formatura.</summary>
    public const int PacotesMaximos = 20;

    /// <summary>Registra as regras de validação.</summary>
    public AderirAoTermoValidator()
    {
        RuleFor(x => x.HashDoConteudo)
            .NotEmpty()
            .WithMessage("Não conseguimos confirmar qual versão do termo você leu. Recarregue a página e tente de novo.")
            .Matches("^[0-9a-f]{64}$")
            .WithMessage("Não conseguimos confirmar qual versão do termo você leu. Recarregue a página e tente de novo.");

        RuleFor(x => x.Codigo)
            .NotEmpty()
            .WithMessage("Informe o código enviado para o seu e-mail.")
            .Matches("^[0-9]{6}$")
            .WithMessage("O código tem seis dígitos.");

        RuleFor(x => x.Pacotes)
            .NotEmpty()
            .WithErrorCode("adesao.cesta_sem_escolha")
            .WithMessage("Escolha ao menos um pacote para aderir.")
            .Must(pacotes => pacotes.Count <= PacotesMaximos)
            .WithMessage($"Escolha até {PacotesMaximos} pacotes.");

        RuleForEach(x => x.Observacoes)
            .Must(observacao => (observacao.Texto?.Trim().Length ?? 0) <= DadosDaSolicitacaoValidator.TamanhoDoTexto)
            .OverridePropertyName("observacoes")
            .WithMessage($"Escreva cada detalhe em até {DadosDaSolicitacaoValidator.TamanhoDoTexto} caracteres.");
    }
}

/// <summary>Forma do aceite do aditivo (Sprint 48, D38): o mesmo rito da adesão.</summary>
public sealed class AceitarAditivoValidator : AbstractValidator<AceitarAditivo>
{
    /// <summary>Registra as regras de validação.</summary>
    public AceitarAditivoValidator()
    {
        RuleFor(x => x.HashDoConteudo)
            .NotEmpty()
            .WithMessage("Não conseguimos confirmar qual versão do aditivo você leu. Recarregue a página e tente de novo.")
            .Matches("^[0-9a-f]{64}$")
            .WithMessage("Não conseguimos confirmar qual versão do aditivo você leu. Recarregue a página e tente de novo.");

        RuleFor(x => x.Codigo)
            .NotEmpty()
            .WithMessage("Informe o código enviado para o seu e-mail.")
            .Matches("^[0-9]{6}$")
            .WithMessage("O código tem seis dígitos.");

        RuleFor(x => x.Pacotes)
            .NotEmpty()
            .WithMessage("Escolha ao menos um pacote para acrescentar.")
            .Must(pacotes => pacotes.Count <= AderirAoTermoValidator.PacotesMaximos)
            .WithMessage($"Escolha até {AderirAoTermoValidator.PacotesMaximos} pacotes.");

        RuleForEach(x => x.Observacoes)
            .Must(observacao => (observacao.Texto?.Trim().Length ?? 0) <= DadosDaSolicitacaoValidator.TamanhoDoTexto)
            .OverridePropertyName("observacoes")
            .WithMessage($"Escreva cada detalhe em até {DadosDaSolicitacaoValidator.TamanhoDoTexto} caracteres.");
    }
}
