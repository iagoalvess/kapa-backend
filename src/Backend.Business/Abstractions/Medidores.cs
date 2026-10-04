namespace Backend.Business.Abstractions;

/// <summary>
/// Nomes dos medidores de telemetria que atravessam API e worker.
/// </summary>
/// <remarks>
/// Ficam no <c>Business</c> porque os dois hosts criam instrumentos com o mesmo nome, e o
/// <c>AddMeter</c> precisa casar exatamente: a API publica o descarte da fila de eventos, o worker
/// publica a profundidade das filas do banco, e os dois sinais têm de cair no mesmo painel.
/// </remarks>
public static class Medidores
{
    /// <summary>Medidor das filas de operação (e-mail, webhook, eventos).</summary>
    public const string Filas = "Kapa.Filas";
}
