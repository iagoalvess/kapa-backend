using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backend.IntegrationTests.Infra;

/// <summary>
/// As opções de serialização que a API usa, para o teste ler e escrever o mesmo contrato.
/// </summary>
/// <remarks>
/// Um lugar só, e não uma cópia por arquivo de teste: a convenção de nomes é contrato, e teste que
/// a declara por conta própria continua verde depois de a API mudar — exatamente quando deveria
/// ficar vermelho.
/// <para>
/// Se estas opções precisarem divergir das da API, é porque o contrato mudou e alguém esqueceu de
/// contar. Mantenha-as iguais a <c>ApiConfig.AddApi</c>.
/// </para>
/// </remarks>
public static class JsonDaApi
{
    /// <summary>Snake_case nas propriedades e nas chaves de dicionário; enum como texto.</summary>
    public static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() },
    };
}
