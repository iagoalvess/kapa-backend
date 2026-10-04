using System.Diagnostics.Metrics;
using Backend.Business.Abstractions;

namespace Backend.Api.Analytics;

/// <summary>
/// Publica o descarte da fila de eventos como métrica.
/// </summary>
/// <remarks>
/// O descarte já era contado e logado pelo <see cref="FlushDeEventosService"/>; faltava o número
/// chegar ao agregador, para o alerta existir sem depender de alguém ler a linha de log. A fila é
/// memória deste processo, então o medidor vive aqui, e não no worker.
/// </remarks>
public sealed class MetricasDeEventos : IDisposable
{
    private readonly Meter _medidor = new(Medidores.Filas);
    private readonly Counter<long> _descartados;

    /// <summary>Cria o contador de descarte.</summary>
    public MetricasDeEventos() => _descartados = _medidor.CreateCounter<long>("kapa.filas.eventos.descartados", unit: "{evento}");

    /// <summary>Soma os eventos descartados por fila cheia desde a última leitura.</summary>
    /// <param name="quantidade">Quantos foram descartados.</param>
    public void RegistrarDescarte(int quantidade)
    {
        if (quantidade > 0)
            _descartados.Add(quantidade);
    }

    /// <inheritdoc />
    public void Dispose() => _medidor.Dispose();
}
