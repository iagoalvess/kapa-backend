namespace Backend.Business.Auth.Settings;

/// <summary>
/// Regras do ciclo de vida da conta.
/// </summary>
public sealed class ContaSettings
{
    /// <summary>Nome da seção correspondente no arquivo de configuração.</summary>
    public const string Secao = "Conta";

    /// <summary>
    /// Exige e-mail confirmado para autenticar.
    /// </summary>
    /// <remarks>
    /// Desligado por padrão de propósito: com <c>Smtp:Host</c> vazio os e-mails só vão para o log,
    /// e ligar isso sem servidor de e-mail configurado trancaria todo mundo para fora no primeiro
    /// cadastro. **Ligue junto com o SMTP**, não antes.
    /// </remarks>
    public bool ExigirEmailConfirmado { get; init; }

    /// <summary>Validade dos links de confirmação e de redefinição, em horas.</summary>
    /// <remarks>
    /// Curta porque o link vai por e-mail, e caixa de entrada é um lugar onde credencial fica
    /// guardada por muito tempo. Vale tanto para confirmação quanto para redefinição.
    /// </remarks>
    public int HorasDeValidadeDoLink { get; init; } = 4;

    /// <summary>Caminho da tela de confirmação de e-mail no front-end.</summary>
    public string CaminhoDeConfirmacao { get; init; } = "/confirmar-email";

    /// <summary>Caminho da tela de redefinição de senha no front-end.</summary>
    public string CaminhoDeRedefinicao { get; init; } = "/redefinir-senha";
}
