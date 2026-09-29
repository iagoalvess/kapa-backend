using Backend.Business.Festa.Validators;
using Backend.Business.Loja.Models;
using FluentValidation;

namespace Backend.Business.Loja.Validators;

/// <summary>
/// Forma do cancelamento da Gestão: o motivo é obrigatório — vai para a auditoria, a portaria e o e-mail (Sprint 38).
/// </summary>
/// <remarks>Serve também à festa cancelada e à recusa do pedido, que só têm o motivo.</remarks>
public sealed class DadosDoCancelamentoValidator : AbstractValidator<DadosDoCancelamento>
{
    /// <summary>Registra as regras de validação.</summary>
    public DadosDoCancelamentoValidator()
    {
        RuleFor(x => x.Motivo)
            .NotEmpty()
            .WithMessage("Diga o motivo do cancelamento.")
            .MaximumLength(DadosDaCortesiaValidator.MotivoMaximo)
            .WithMessage($"O motivo deve ter no máximo {DadosDaCortesiaValidator.MotivoMaximo} caracteres.");
    }
}

/// <summary>Forma do pedido do comprador: o motivo é opcional (P1).</summary>
public sealed class PedidoDoCompradorValidator : AbstractValidator<PedidoDoComprador>
{
    /// <summary>Registra as regras de validação.</summary>
    public PedidoDoCompradorValidator()
    {
        RuleFor(x => x.Motivo)
            .MaximumLength(DadosDaCortesiaValidator.MotivoMaximo)
            .WithMessage($"O motivo deve ter no máximo {DadosDaCortesiaValidator.MotivoMaximo} caracteres.");
    }
}
