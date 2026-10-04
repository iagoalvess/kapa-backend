using System.Diagnostics.Metrics;
using Backend.Api.Analytics;
using Backend.Business.Abstractions;
using Backend.Business.Emails.Interfaces;
using Backend.Worker.Metricas;
using Shouldly;

namespace Backend.UnitTests.Worker;

/// <summary>
/// As métricas de fila precisam publicar o último retrato, não um valor de nascença: é o número que
/// o alerta da Sprint 16 vai ler.
/// </summary>
public sealed class MetricasDeOperacaoTests
{
    [Fact]
    public void Gauge_das_filas_publica_o_ultimo_retrato()
    {
        // Arrange
        var agora = DateTime.UtcNow;
        using var metricas = new MetricasDeFilas();
        var valores = new Dictionary<string, long>();

        using var ouvinte = new MeterListener();
        ouvinte.InstrumentPublished = (instrumento, escuta) =>
        {
            if (instrumento.Meter.Name == Medidores.Filas)
                escuta.EnableMeasurementEvents(instrumento);
        };
        ouvinte.SetMeasurementEventCallback<long>((instrumento, valor, _, _) => valores[instrumento.Name] = valor);
        ouvinte.Start();

        // Act
        metricas.AtualizarEmail(new ProfundidadeDaFilaDeEmail(7, 2, agora.AddMinutes(-30)), agora);
        metricas.AtualizarWebhook(agora.AddMinutes(-5), agora);
        ouvinte.RecordObservableInstruments();

        // Assert
        valores["kapa.filas.email.pendentes"].ShouldBe(7);
        valores["kapa.filas.email.presos"].ShouldBe(2);
        valores["kapa.filas.email.minutos_do_mais_antigo"].ShouldBe(30);
        valores["kapa.filas.webhook.minutos_desde_o_ultimo"].ShouldBe(5);
    }

    [Fact]
    public void Contador_de_descarte_soma_o_que_foi_descartado()
    {
        // Arrange
        using var metricas = new MetricasDeEventos();
        long total = 0;

        using var ouvinte = new MeterListener();
        ouvinte.InstrumentPublished = (instrumento, escuta) =>
        {
            if (instrumento.Meter.Name == Medidores.Filas)
                escuta.EnableMeasurementEvents(instrumento);
        };
        ouvinte.SetMeasurementEventCallback<long>((_, valor, _, _) => total += valor);
        ouvinte.Start();

        // Act
        metricas.RegistrarDescarte(0);
        metricas.RegistrarDescarte(3);
        metricas.RegistrarDescarte(4);

        // Assert
        total.ShouldBe(7);
    }
}
