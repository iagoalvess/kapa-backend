namespace Backend.Business.Assinaturas.Settings;

/// <summary>
/// Configuração da cobrança da licença.
/// </summary>
/// <remarks>
/// Sem <see cref="SegredoDoWebhook"/>, todo webhook é recusado: aceitar corpo sem assinatura
/// verificável é deixar qualquer um com a URL ativar a própria formatura de graça.
/// </remarks>
public sealed class AssinaturaSettings
{
    /// <summary>Nome da seção correspondente no arquivo de configuração.</summary>
    public const string Secao = "Assinaturas";

    /// <summary>Segredo compartilhado com o provedor para o HMAC do webhook.</summary>
    public string SegredoDoWebhook { get; init; } = string.Empty;

    /// <summary>Cabeçalho em que o provedor manda a assinatura HMAC do corpo.</summary>
    public string CabecalhoDaAssinatura { get; init; } = "X-Assinatura";

    /// <summary>Rota do front para onde o provedor devolve o navegador.</summary>
    public string CaminhoDeRetorno { get; init; } = "/assinatura/retorno";

    /// <summary>Tolerância depois do vencimento antes de suspender a formatura.</summary>
    public int DiasDeCarencia { get; init; } = 7;

    /// <summary>Quanto uma assinatura fica pendente antes de a conciliação perguntar ao provedor.</summary>
    public int MinutosAntesDeConciliar { get; init; } = 30;

    /// <summary>Configuração do provedor fake.</summary>
    public ProvedorFakeSettings Fake { get; init; } = new();
}

/// <summary>Configuração do provedor fake.</summary>
public sealed class ProvedorFakeSettings
{
    /// <summary>Endereço público da API, onde a página de pagamento fake é servida.</summary>
    public string UrlDaApi { get; init; } = "https://localhost:7104";

    /// <summary>Se o pagamento na página fake dispara o webhook. Desligado, simula o webhook que se perdeu.</summary>
    public bool EntregarWebhook { get; init; } = true;
}
