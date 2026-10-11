namespace Backend.Business.Auth.Settings;

/// <summary>Configuração opcional do login Google; vazio preserva apenas a senha.</summary>
public sealed class GoogleSettings
{
    /// <summary>Seção de configuração.</summary>
    public const string Secao = "Google";

    /// <summary>Client ID público do projeto Google Cloud do Kapa.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Se o provedor foi configurado.</summary>
    public bool Habilitado => !string.IsNullOrWhiteSpace(ClientId);
}
