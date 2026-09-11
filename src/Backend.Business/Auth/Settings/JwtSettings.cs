using System.ComponentModel.DataAnnotations;

namespace Backend.Business.Auth.Settings;

/// <summary>
/// Configuração de emissão e validação de token.
/// </summary>
/// <remarks>
/// Validada na subida da aplicação (<c>ValidateDataAnnotations().ValidateOnStart()</c>): se a
/// chave estiver ausente ou curta demais, o processo não sobe. Falhar no boot é muito melhor
/// que descobrir em produção que a API assinou tokens com string vazia.
/// <para>
/// <b>A chave nunca vai para o appsettings versionado.</b> Em desenvolvimento use
/// <c>dotnet user-secrets</c>; em produção, variável de ambiente
/// <c>Jwt__ChaveSecreta</c> ou cofre de segredos.
/// </para>
/// </remarks>
public sealed class JwtSettings
{
    /// <summary>Nome da seção correspondente no arquivo de configuração.</summary>
    public const string Secao = "Jwt";

    /// <summary>Quem emite o token.</summary>
    [Required(ErrorMessage = "Jwt:Emissor é obrigatório.")]
    public string Emissor { get; init; } = string.Empty;

    /// <summary>Para quem o token se destina.</summary>
    [Required(ErrorMessage = "Jwt:Audiencia é obrigatório.")]
    public string Audiencia { get; init; } = string.Empty;

    /// <summary>
    /// Chave simétrica de assinatura. Mínimo de 32 caracteres porque HMAC-SHA256 exige
    /// 256 bits — chave menor faz a biblioteca lançar em tempo de execução.
    /// </summary>
    [Required(ErrorMessage = "Jwt:ChaveSecreta é obrigatório.")]
    [MinLength(32, ErrorMessage = "Jwt:ChaveSecreta precisa de ao menos 32 caracteres (256 bits) para HMAC-SHA256.")]
    public string ChaveSecreta { get; init; } = string.Empty;

    /// <summary>
    /// Validade do access token, em minutos. Curta de propósito: o access token não é
    /// revogável, então a janela entre revogar o acesso e ele deixar de funcionar é esta.
    /// </summary>
    [Range(1, 120, ErrorMessage = "Jwt:MinutosDeValidadeDoAccessToken deve ficar entre 1 e 120.")]
    public int MinutosDeValidadeDoAccessToken { get; init; } = 15;

    /// <summary>Validade do refresh token, em dias. É ele quem define por quanto tempo a sessão dura.</summary>
    [Range(1, 90, ErrorMessage = "Jwt:DiasDeValidadeDoRefreshToken deve ficar entre 1 e 90.")]
    public int DiasDeValidadeDoRefreshToken { get; init; } = 7;
}
