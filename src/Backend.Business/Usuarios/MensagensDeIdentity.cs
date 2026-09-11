using Microsoft.AspNetCore.Identity;

namespace Backend.Business.Usuarios;

/// <summary>
/// Mensagens do ASP.NET Identity em português.
/// </summary>
/// <remarks>
/// O Identity descreve os próprios erros em inglês, e essas mensagens **chegam ao usuário
/// final**: sem isto, quem erra a senha no cadastro lê "Passwords must have at least one digit".
/// Traduzir aqui é melhor que interceptar no controller, porque cobre qualquer caminho que use
/// o <c>UserManager</c>, inclusive os que ainda não existem.
/// <para>Só os códigos que o projeto realmente pode disparar são sobrescritos.</para>
/// </remarks>
public sealed class MensagensDeIdentity : IdentityErrorDescriber
{
    /// <inheritdoc />
    public override IdentityError DuplicateEmail(string email) => Erro(nameof(DuplicateEmail), "Já existe uma conta com este e-mail.");

    /// <inheritdoc />
    public override IdentityError DuplicateUserName(string userName) => Erro(nameof(DuplicateUserName), "Já existe uma conta com este e-mail.");

    /// <inheritdoc />
    public override IdentityError InvalidEmail(string? email) => Erro(nameof(InvalidEmail), "O e-mail informado não é válido.");

    /// <inheritdoc />
    public override IdentityError PasswordTooShort(int length) => Erro(nameof(PasswordTooShort), $"A senha deve ter no mínimo {length} caracteres.");

    /// <inheritdoc />
    public override IdentityError PasswordRequiresDigit() => Erro(nameof(PasswordRequiresDigit), "A senha deve conter ao menos um número.");

    /// <inheritdoc />
    public override IdentityError PasswordRequiresLower() => Erro(nameof(PasswordRequiresLower), "A senha deve conter ao menos uma letra minúscula.");

    /// <inheritdoc />
    public override IdentityError PasswordRequiresUpper() => Erro(nameof(PasswordRequiresUpper), "A senha deve conter ao menos uma letra maiúscula.");

    /// <inheritdoc />
    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Erro(nameof(PasswordRequiresNonAlphanumeric), "A senha deve conter ao menos um caractere especial.");

    /// <inheritdoc />
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Erro(nameof(PasswordRequiresUniqueChars), $"A senha deve conter ao menos {uniqueChars} caracteres diferentes.");

    /// <inheritdoc />
    public override IdentityError PasswordMismatch() => Erro(nameof(PasswordMismatch), "A senha atual está incorreta.");

    /// <inheritdoc />
    public override IdentityError UserAlreadyInRole(string role) => Erro(nameof(UserAlreadyInRole), $"O usuário já possui o perfil {role}.");

    /// <inheritdoc />
    public override IdentityError UserNotInRole(string role) => Erro(nameof(UserNotInRole), $"O usuário não possui o perfil {role}.");

    /// <inheritdoc />
    public override IdentityError InvalidToken() => Erro(nameof(InvalidToken), "O código informado é inválido ou expirou.");

    private static IdentityError Erro(string codigo, string descricao) => new() { Code = codigo, Description = descricao };
}
