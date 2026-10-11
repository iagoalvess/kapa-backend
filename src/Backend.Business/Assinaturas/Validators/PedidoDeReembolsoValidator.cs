using Backend.Business.Assinaturas.Models;
using FluentValidation;

namespace Backend.Business.Assinaturas.Validators;

/// <summary>Forma do motivo necessário para analisar o pedido.</summary>
public sealed class PedidoDeReembolsoValidator : AbstractValidator<PedidoDeReembolsoDados>
{
    /// <summary>Motivo obrigatório, limitado a 2.000 caracteres.</summary>
    public PedidoDeReembolsoValidator()
    {
        RuleFor(p => p.Motivo).NotEmpty().WithMessage("Informe o motivo do pedido.").MaximumLength(2000);
    }
}
