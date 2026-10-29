using FluentValidation;

namespace Backend.Business.Auth.Validators;

/// <summary>
/// Teto de tamanho da senha.
/// </summary>
/// <remarks>
/// Não é política de força — o mínimo e a composição vivem no <c>IdentityOptions</c>. É teto de
/// sanidade para o corpo que chega ao hash; 128 cabe em qualquer senha de gerenciador.
/// </remarks>
internal static class LimitesDaSenha
{
    /// <summary>Maior senha aceita, em caracteres.</summary>
    public const int Maximo = 128;
}

/// <summary>
/// Regras de forma repetidas pelos pedidos de conta.
/// </summary>
internal static class RegrasDeConta
{
    /// <summary>E-mail obrigatório e bem formado.</summary>
    public static IRuleBuilderOptions<T, string> EmailObrigatorio<T>(this IRuleBuilder<T, string> regra) =>
        regra.NotEmpty().WithMessage("O e-mail é obrigatório.").EmailAddress().WithMessage("Informe um e-mail válido.");
}
