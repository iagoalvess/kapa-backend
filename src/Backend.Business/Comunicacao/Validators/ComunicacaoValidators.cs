using Backend.Business.Comunicacao.Models;
using FluentValidation;

namespace Backend.Business.Comunicacao.Validators;

/// <summary>Os limites comuns ao aviso e ao documento.</summary>
internal static class LimitesDaComunicacao
{
    /// <summary>Título do aviso e do documento — uma linha de cartão.</summary>
    public const int TituloMaximo = 150;

    /// <summary>Texto do aviso: um comunicado longo cabe; um livro, não.</summary>
    public const int ConteudoMaximo = 20_000;

    /// <summary>Visibilidade escolhida e conhecida.</summary>
    public static IRuleBuilderOptions<T, Visibilidade?> VisibilidadeEscolhida<T>(this IRuleBuilderInitial<T, Visibilidade?> regra) =>
        regra
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Escolha para quem é: a turma toda ou só a comissão.")
            .IsInEnum()
            .WithMessage("Visibilidade desconhecida.");

    /// <summary>Título presente e numa linha.</summary>
    public static IRuleBuilderOptions<T, string> Titulo<T>(this IRuleBuilderInitial<T, string> regra) =>
        regra
            .NotEmpty()
            .WithMessage("Informe o título.")
            .MaximumLength(TituloMaximo)
            .WithMessage($"O título deve ter no máximo {TituloMaximo} caracteres.");
}

/// <summary>Forma do aviso.</summary>
public sealed class DadosDoAvisoValidator : AbstractValidator<DadosDoAviso>
{
    /// <summary>Registra as regras de validação.</summary>
    public DadosDoAvisoValidator()
    {
        RuleFor(x => x.Titulo).Titulo();

        RuleFor(x => x.Conteudo)
            .NotEmpty()
            .WithMessage("Escreva o texto do aviso.")
            .MaximumLength(LimitesDaComunicacao.ConteudoMaximo)
            .WithMessage($"O texto deve ter no máximo {LimitesDaComunicacao.ConteudoMaximo} caracteres.");

        RuleFor(x => x.Visibilidade).VisibilidadeEscolhida();
    }
}

/// <summary>Forma do documento.</summary>
/// <remarks>Categoria obrigatória: é o que impede o acervo de virar lixeira de arquivos.</remarks>
public sealed class DadosDoDocumentoValidator : AbstractValidator<DadosDoDocumento>
{
    /// <summary>Registra as regras de validação.</summary>
    public DadosDoDocumentoValidator()
    {
        RuleFor(x => x.Titulo).Titulo();

        RuleFor(x => x.Categoria)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Escolha a categoria do documento.")
            .IsInEnum()
            .WithMessage("Categoria desconhecida.");

        RuleFor(x => x.Visibilidade).VisibilidadeEscolhida();
    }
}
