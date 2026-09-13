using FluentValidation;
using FluentValidation.Results;

namespace Backend.Business.Abstractions;

/// <summary>
/// Ponte entre o FluentValidation e o <see cref="Result"/>.
/// </summary>
public static class ValidacaoExtensions
{
    /// <summary>
    /// Valida a instância e devolve um <see cref="Result"/> carregando **todos** os erros.
    /// </summary>
    /// <remarks>
    /// Devolve a lista inteira, não o primeiro erro: um formulário com três campos inválidos
    /// deve acender os três de uma vez, e não obrigar o usuário a três idas ao servidor.
    /// </remarks>
    /// <typeparam name="T">Tipo validado.</typeparam>
    /// <param name="validator">Validador a aplicar.</param>
    /// <param name="instancia">Instância a validar.</param>
    public static Result Validar<T>(this IValidator<T> validator, T instancia)
    {
        var resultado = validator.Validate(instancia);

        return resultado.IsValid ? Result.Ok() : Result.Falha(resultado.ParaErros());
    }

    /// <summary>Converte as falhas do FluentValidation em erros do domínio.</summary>
    /// <remarks>
    /// Regra sem <c>WithErrorCode</c> sai com o nome do validador (<c>NotEmptyValidator</c>), que
    /// não é contrato de ninguém: vira <c>validacao.invalido</c>. Só código no formato
    /// <c>recurso.motivo</c> passa adiante.
    /// </remarks>
    /// <param name="resultado">Resultado de validação.</param>
    public static IReadOnlyList<Erro> ParaErros(this ValidationResult resultado) =>
        [.. resultado.Errors.Select(falha => Erro.Validacao(CodigoDe(falha), falha.ErrorMessage, ParaCamelCase(falha.PropertyName)))];

    private static string CodigoDe(ValidationFailure falha) => falha.ErrorCode?.Contains('.') == true ? falha.ErrorCode : "validacao.invalido";

    private static string ParaCamelCase(string propriedade)
    {
        if (string.IsNullOrEmpty(propriedade))
            return propriedade;

        return string.Join('.', propriedade.Split('.').Select(parte => char.ToLowerInvariant(parte[0]) + parte[1..]));
    }
}
