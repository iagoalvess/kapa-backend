using System.Diagnostics.Metrics;
using Backend.Business.Abstractions;
using Backend.Business.Emails.Interfaces;

namespace Backend.Worker.Metricas;

/// <summary>
/// Guarda o último retrato das filas do banco e o publica como <i>gauge</i>.
/// </summary>
/// <remarks>
/// O <c>MetricasDeFilasJob</c> consulta o banco e chama os <c>Atualizar*</c>; o instrumento
/// lê os campos no momento da coleta. O callback de um <c>ObservableGauge</c> é síncrono — não dá para
/// consultar o banco ali —, e é por isso que o valor é publicado, não buscado.
/// <para>
/// Singleton, como o medidor: um por processo. Cada réplica do worker mede o mesmo banco e publica o
/// seu ponto; o agregador decide o que fazer com a duplicata.
/// </para>
/// </remarks>
public sealed class MetricasDeFilas : IDisposable
{
    private readonly Meter _medidor = new(Medidores.Filas);

    private long _emailsPendentes;
    private long _emailsPresos;
    private long _minutosDoPendenteMaisAntigo;
    private long _minutosDesdeOUltimoWebhook;

    /// <summary>Cria o medidor e os instrumentos.</summary>
    public MetricasDeFilas()
    {
        _medidor.CreateObservableGauge("kapa.filas.email.pendentes", () => Volatile.Read(ref _emailsPendentes), unit: "{email}");
        _medidor.CreateObservableGauge("kapa.filas.email.presos", () => Volatile.Read(ref _emailsPresos), unit: "{email}");
        _medidor.CreateObservableGauge("kapa.filas.email.minutos_do_mais_antigo", () => Volatile.Read(ref _minutosDoPendenteMaisAntigo), unit: "min");
        _medidor.CreateObservableGauge(
            "kapa.filas.webhook.minutos_desde_o_ultimo",
            () => Volatile.Read(ref _minutosDesdeOUltimoWebhook),
            unit: "min"
        );
    }

    /// <summary>Publica o retrato da fila de e-mails.</summary>
    /// <param name="profundidade">Contagem e idade lidas do banco.</param>
    /// <param name="agoraUtc">Relógio da leitura, para calcular a idade.</param>
    public void AtualizarEmail(ProfundidadeDaFilaDeEmail profundidade, DateTime agoraUtc)
    {
        Volatile.Write(ref _emailsPendentes, profundidade.Pendentes);
        Volatile.Write(ref _emailsPresos, profundidade.Presos);
        Volatile.Write(ref _minutosDoPendenteMaisAntigo, IdadeEmMinutos(profundidade.MaisAntigoEm, agoraUtc));
    }

    /// <summary>Publica há quantos minutos chegou o último webhook de cobrança.</summary>
    /// <param name="ultimoRecebidoEm">Instante do último evento, ou nulo se nunca chegou nenhum.</param>
    /// <param name="agoraUtc">Relógio da leitura.</param>
    public void AtualizarWebhook(DateTime? ultimoRecebidoEm, DateTime agoraUtc) =>
        Volatile.Write(ref _minutosDesdeOUltimoWebhook, IdadeEmMinutos(ultimoRecebidoEm, agoraUtc));

    /// <summary>Sem evento nenhum, devolve zero — "nunca chegou" é caso do alerta, não do gauge.</summary>
    private static long IdadeEmMinutos(DateTime? desdeUtc, DateTime agoraUtc) =>
        desdeUtc is { } desde ? (long)Math.Max(0, (agoraUtc - desde).TotalMinutes) : 0;

    /// <inheritdoc />
    public void Dispose() => _medidor.Dispose();
}
