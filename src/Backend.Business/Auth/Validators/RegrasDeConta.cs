using FluentValidation;

namespace Backend.Business.Auth.Validators;

/// <summary>
/// Regras de forma repetidas pelos pedidos de conta.
/// </summary>
internal static class RegrasDeConta
{
    /// <summary>E-mail obrigatório e bem formado.</summary>
    public static IRuleBuilderOptions<T, string> EmailObrigatorio<T>(this IRuleBuilder<T, string> regra) =>
        regra.NotEmpty().WithMessage("O e-mail é obrigatório.").EmailAddress().WithMessage("Informe um e-mail válido.");
}
