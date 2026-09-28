namespace Backend.Business.IA.Settings;

/// <summary>
/// Seção <c>IA</c>: o provedor de modelos de linguagem que toda feature de IA usa.
/// </summary>
/// <remarks>
/// Um provedor, uma chave, para a plataforma inteira; o que muda de feature para feature — os modelos,
/// o teto por rodada — mora na seção da própria feature (ex.: <c>ResumoDoTermo</c>). Qualquer API no
/// formato <c>chat/completions</c> da OpenAI serve: OpenRouter hoje, trocar é mudar
/// <see cref="BaseUrl"/> e os ids dos modelos.
/// <para>
/// <see cref="ApiKey"/> vazia desliga toda IA, como o <c>Smtp:Host</c> vazio: sobe sem chave, sem
/// chamada HTTP e sem erro no log. A chave só vai no <c>appsettings</c> do worker (Sprint 24, decisão
/// 11) — a API lê o que o worker gravou e não tem o que fazer com ela.
/// </para>
/// </remarks>
public sealed class IaSettings
{
    /// <summary>Nome da seção na configuração.</summary>
    public const string Secao = "IA";

    /// <summary>Raiz da API no formato da OpenAI, terminada em barra.</summary>
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1/";

    /// <summary>Chave do provedor. Vazia: IA desligada.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Quanto esperar cada modelo, em segundos. Modelo gratuito sob carga demora.</summary>
    public int SegundosDeEspera { get; init; } = 120;

    /// <summary>Se há chave — sem ela, nenhuma feature de IA faz nada.</summary>
    public bool Ligado => !string.IsNullOrWhiteSpace(ApiKey);
}
