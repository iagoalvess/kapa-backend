using System.Text.Json;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Adesoes.Models;

/// <summary>
/// O que o aditivo acrescenta à cesta, congelado no instante do aceite (Sprint 48, D38).
/// </summary>
/// <remarks>
/// Só acrescenta: subir de faixa no mesmo grupo ou incluir um pacote novo. Descer ou tirar é solicitação de
/// cancelamento (D8). Gravado como JSON com <see cref="VersaoDoEsquema"/>, como o <see cref="SnapshotDoPlano"/>, e
/// serializado do mesmo jeito — o hash é calculado sobre este texto.
/// </remarks>
/// <param name="VersaoDoEsquema">Forma deste JSON.</param>
/// <param name="PlanoId">Plano vigente no dia.</param>
/// <param name="VersaoDoTermo">A versão do termo que o aditivo emenda.</param>
/// <param name="Mudancas">Um pacote que entra por linha, com o que ele substitui.</param>
/// <param name="Parcelas">As parcelas novas, por vencimento.</param>
/// <param name="TotalEmCentavos">Soma das parcelas novas — o que o formando passa a dever a mais.</param>
public sealed record SnapshotDoAditivo(
    int VersaoDoEsquema,
    Guid PlanoId,
    int VersaoDoTermo,
    IReadOnlyList<MudancaDaCesta> Mudancas,
    IReadOnlyList<ParcelaSimulada> Parcelas,
    long TotalEmCentavos
)
{
    /// <summary>Versão atual da forma do JSON.</summary>
    public const int EsquemaAtual = 1;

    /// <summary>Lê um snapshot gravado.</summary>
    /// <param name="json">Texto como está no aditivo.</param>
    public static SnapshotDoAditivo Ler(string json) =>
        JsonSerializer.Deserialize<SnapshotDoAditivo>(json, SnapshotDoPlano.Json)
        ?? throw new InvalidOperationException("Snapshot de aditivo vazio.");

    /// <summary>O texto que vai para o aditivo — e para o hash do aceite.</summary>
    public string ParaJson() => JsonSerializer.Serialize(this, SnapshotDoPlano.Json);
}

/// <summary>Um pacote que entra na cesta pelo aditivo.</summary>
/// <param name="Entra">O pacote novo, com o preço do catálogo no dia.</param>
/// <param name="Sai">A faixa do mesmo grupo que ele substitui; nulo quando é pacote novo.</param>
/// <param name="JaContratadoEmCentavos">O que o grupo da faixa que sai já soma nas parcelas do formando — o que não se cobra de novo.</param>
/// <param name="DiferencaEmCentavos">O que o aditivo cobra por este pacote: o preço menos o já contratado.</param>
public sealed record MudancaDaCesta(PacoteDaCesta Entra, PacoteDaCesta? Sai, long JaContratadoEmCentavos, long DiferencaEmCentavos);
