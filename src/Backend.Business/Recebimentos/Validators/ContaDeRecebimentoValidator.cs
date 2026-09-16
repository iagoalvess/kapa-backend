using Backend.Business.Recebimentos.Models;
using Backend.Business.Recebimentos.Services;
using FluentValidation;

namespace Backend.Business.Recebimentos.Validators;

/// <summary>
/// Forma da chave PIX, do titular e da cidade.
/// </summary>
/// <remarks>
/// A chave é conferida pelo tipo, com a mensagem que diz o que corrigir. Nome e cidade precisam
/// sobrar no BR Code depois de perder o acento e o que não é ASCII — um nome só de ideogramas
/// viraria um campo vazio, e o banco recusaria o QR.
/// </remarks>
public sealed class ContaDeRecebimentoValidator : AbstractValidator<DadosDaConta>
{
    private static readonly Dictionary<TipoDeChavePix, string> MotivoPorTipo = new()
    {
        [TipoDeChavePix.Cpf] = "CPF inválido: confira os 11 dígitos.",
        [TipoDeChavePix.Cnpj] = "CNPJ inválido: confira os 14 caracteres.",
        [TipoDeChavePix.Email] = "E-mail inválido.",
        [TipoDeChavePix.Telefone] = "Informe o celular com DDD, como (41) 99876-5432.",
        [TipoDeChavePix.Aleatoria] = "A chave aleatória tem o formato 123e4567-e89b-12d3-a456-426614174000.",
    };

    /// <summary>Registra as regras de validação.</summary>
    public ContaDeRecebimentoValidator()
    {
        RuleFor(x => x.TipoDeChave).IsInEnum().WithMessage("Tipo de chave inválido. Use Cpf, Cnpj, Email, Telefone ou Aleatoria.");

        RuleFor(x => x.Chave)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Informe a chave PIX.")
            .Must((dados, chave) => ChavePix.Normalizar(dados.TipoDeChave, chave) is not null)
            .WithMessage(dados => MotivoPorTipo.GetValueOrDefault(dados.TipoDeChave, "Chave inválida."))
            .When(x => Enum.IsDefined(x.TipoDeChave));

        RuleFor(x => x.NomeDoTitular)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Informe o nome do titular, como o banco mostra.")
            .MaximumLength(200)
            .WithMessage("O nome deve ter no máximo 200 caracteres.")
            .Must(nome => BrCode.Texto(nome, BrCode.TamanhoMaximoDoNome).Length > 0)
            .WithMessage("Escreva o nome com letras do alfabeto latino.");

        RuleFor(x => x.Cidade)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Informe a cidade do titular.")
            .MaximumLength(100)
            .WithMessage("A cidade deve ter no máximo 100 caracteres.")
            .Must(cidade => BrCode.Texto(cidade, BrCode.TamanhoMaximoDaCidade).Length > 0)
            .WithMessage("Escreva a cidade com letras do alfabeto latino.");
    }
}
