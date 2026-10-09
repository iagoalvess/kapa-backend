using Backend.Business.Cobrancas.Validators;
using Backend.Business.Common.Texto;
using Backend.Business.Common.Validacao;
using Backend.Business.Festa.Validators;
using Backend.Business.Loja.Models;
using Backend.Business.Pagamentos.Models;
using FluentValidation;

namespace Backend.Business.Loja.Validators;

/// <summary>
/// Forma da compra: nome, e-mail, CPF com dígito verificador (P6), um meio que a loja oferece e o titular de cada
/// convite — nome e documento já na compra, sem convite "a definir".
/// </summary>
/// <remarks>
/// Roda antes de qualquer consulta: o CPF inválido é recusado antes da reserva, e nada da compra chega à
/// trava do item sem ter passado por aqui.
/// </remarks>
public sealed class DadosDaCompraValidator : AbstractValidator<DadosDaCompra>
{
    /// <summary>Registra as regras de validação.</summary>
    public DadosDaCompraValidator()
    {
        RuleFor(x => x.ItemDeCobrancaId).NotEmpty().OverridePropertyName("item_de_cobranca_id").WithMessage("Escolha o convite.");

        RuleFor(x => x.Quantidade)
            .InclusiveBetween(1, DadosDoOpcionalValidator.EstoqueMaximo)
            .OverridePropertyName("quantidade")
            .WithMessage("Informe uma quantidade de ao menos 1.");

        RuleFor(x => x.Nome).NomeDaPessoa().OverridePropertyName("nome");

        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("O e-mail é obrigatório.")
            .EmailAddress()
            .WithMessage("Informe um e-mail válido.")
            .MaximumLength(200)
            .WithMessage("O e-mail deve ter no máximo 200 caracteres.")
            .OverridePropertyName("email");

        RuleFor(x => x.Cpf)
            .Must(FormatosBrasileiros.CpfValido)
            .OverridePropertyName("cpf")
            .WithErrorCode("loja.cpf_invalido")
            .WithMessage("CPF inválido. Confira os números.");

        RuleFor(x => x.Meio)
            .Must(MeiosDePagamento.Ligados.Contains)
            .OverridePropertyName("meio")
            .WithMessage("Escolha como pagar entre as opções da loja.");

        RuleFor(x => x.Convidados)
            .Must((compra, convidados) => convidados.Count == compra.Quantidade)
            .OverridePropertyName("convidados")
            .WithMessage("Diga quem vai usar cada convite.");

        RuleForEach(x => x.Convidados)
            .SetValidator(new DadosDoConvidadoValidator())
            .ChildRules(convidado =>
                convidado.RuleFor(c => c.NumeroDoDocumento).NotEmpty().WithMessage("Informe o documento de quem vai usar o convite.")
            );

        RuleFor(x => x.LeuAPolitica)
            .Equal(true)
            .OverridePropertyName("leu_a_politica")
            .WithErrorCode("loja.politica_nao_lida")
            .WithMessage("Confirme que leu como seus dados são usados.");

        RuleFor(x => x.ChaveDeIdempotencia)
            .NotEmpty()
            .OverridePropertyName("chave_de_idempotencia")
            .WithMessage("Envie a chave de idempotência gerada ao abrir o formulário.");
    }
}
