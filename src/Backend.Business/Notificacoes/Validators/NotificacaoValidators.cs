using Backend.Business.Notificacoes.Models;
using Backend.Business.Notificacoes.Services;
using FluentValidation;

namespace Backend.Business.Notificacoes.Validators;

/// <summary>
/// Forma da régua gravada de uma vez.
/// </summary>
/// <remarks>
/// A variável desconhecida é recusada <b>aqui</b>, na gravação, e não no envio (critério de aceite):
/// <c>{vencimeto}</c> descoberto na hora do disparo é oitenta e-mails errados já na caixa de entrada.
/// </remarks>
public sealed class DadosDaReguaValidator : AbstractValidator<DadosDaRegua>
{
    /// <summary>Registra as regras de validação.</summary>
    public DadosDaReguaValidator()
    {
        RuleFor(x => x.Regras).NotEmpty().WithMessage("A régua precisa de pelo menos um degrau.");

        RuleFor(x => x.Regras)
            .Must(regras => regras.Select(r => (r.Gatilho, r.DiasDeDeslocamento)).Distinct().Count() == regras.Count)
            .WithMessage("Há dois degraus para o mesmo gatilho e o mesmo deslocamento.")
            .When(x => x.Regras is { Count: > 0 });

        RuleForEach(x => x.Regras).SetValidator(new DadosDaRegraValidator());
    }
}

/// <summary>Forma de um degrau da régua.</summary>
public sealed class DadosDaRegraValidator : AbstractValidator<DadosDaRegra>
{
    /// <summary>Deslocamento aceito: de trinta dias antes do vencimento a um ano depois.</summary>
    private const int DeslocamentoMaximo = 365;

    /// <summary>Registra as regras de validação.</summary>
    public DadosDaRegraValidator()
    {
        RuleFor(x => x.Gatilho).IsInEnum().WithMessage("Gatilho desconhecido.");
        RuleFor(x => x.Canal).IsInEnum().WithMessage("Canal desconhecido.");

        RuleFor(x => x.DiasDeDeslocamento)
            .InclusiveBetween(-30, DeslocamentoMaximo)
            .WithMessage($"O deslocamento vai de 30 dias antes do vencimento a {DeslocamentoMaximo} dias depois.");

        RuleFor(x => x.DiasDeDeslocamento)
            .GreaterThan(0)
            .WithMessage("O aviso de informe parado conta dias de espera: use um número positivo.")
            .When(x => x.Gatilho is GatilhoDaRegua.InformePendente);

        RuleFor(x => x.Assunto).Texto(TemplateDeNotificacao.TamanhoMaximoDoAssunto, "o assunto");
        RuleFor(x => x.Template).Texto(TemplateDeNotificacao.TamanhoMaximo, "a mensagem");
    }
}

/// <summary>Forma das preferências do titular.</summary>
public sealed class DadosDasPreferenciasValidator : AbstractValidator<DadosDasPreferencias>
{
    /// <summary>Registra as regras de validação.</summary>
    public DadosDasPreferenciasValidator()
    {
        RuleFor(x => x.Preferencias).NotNull().WithMessage("Informe as preferências.");

        RuleForEach(x => x.Preferencias).ChildRules(item => item.RuleFor(p => p.Tipo).IsInEnum().WithMessage("Tipo de notificação desconhecido."));
    }
}

/// <summary>Texto de template: presente, dentro do teto e sem variável inventada.</summary>
internal static class TextoDeTemplate
{
    /// <summary>Valida assunto ou corpo de um degrau.</summary>
    /// <typeparam name="T">O objeto validado.</typeparam>
    /// <param name="regra">Regra em construção.</param>
    /// <param name="maximo">Teto de caracteres.</param>
    /// <param name="nome">Como o campo se chama na mensagem de erro.</param>
    public static IRuleBuilderOptions<T, string> Texto<T>(this IRuleBuilderInitial<T, string> regra, int maximo, string nome) =>
        regra
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage($"Escreva {nome}.")
            .MaximumLength(maximo)
            .WithMessage($"{char.ToUpperInvariant(nome[0])}{nome[1..]} deve ter no máximo {maximo} caracteres.")
            .Must(texto => TemplateDeNotificacao.Desconhecidas(texto).Count == 0)
            .WithMessage(
                (_, texto) =>
                    $"Variável desconhecida: {Entre(TemplateDeNotificacao.Desconhecidas(texto))}. As disponíveis são {Entre(TemplateDeNotificacao.Variaveis)}."
            );

    private static string Entre(IReadOnlyList<string> variaveis) => string.Join(", ", variaveis.Select(v => $"{{{v}}}"));
}
