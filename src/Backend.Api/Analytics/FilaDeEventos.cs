using System.Threading.Channels;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;

namespace Backend.Api.Analytics;

/// <summary>
/// Fila em memória entre quem produz o evento e quem o grava.
/// </summary>
/// <remarks>
/// Canal <b>limitado</b> com descarte na escrita. As duas escolhas são deliberadas:
/// <list type="bullet">
/// <item>limitado, porque fila ilimitada não é fila — é um vazamento de memória com nome
/// bonito, que derruba o processo quando o banco fica lento;</item>
/// <item>descarte na escrita, porque a alternativa é fazer a requisição do usuário esperar por
/// espaço numa fila de analytics. Perder um ponto do gráfico é aceitável; atrasar a operação
/// que estava sendo medida, não.</item>
/// </list>
/// <para>
/// O que foi descartado é contado e sai no log pelo serviço de descarga — descarte silencioso
/// viraria um gráfico errado em que ninguém desconfia.
/// </para>
/// <para>
/// <b>É memória de um processo.</b> O que estiver na fila no momento de um encerramento abrupto
/// se perde, e cada réplica tem a sua. Para evento que não pode ser perdido — cobrança,
/// auditoria legal — grave na transação do domínio, não aqui.
/// </para>
/// </remarks>
public sealed class FilaDeEventos : IRegistradorDeEventos
{
    private const int CapacidadeMaxima = 10_000;

    private readonly Channel<Evento> _canal;

    private int _descartados;

    /// <summary>Inicializa a fila.</summary>
    /// <remarks>
    /// O descarte é contado pelo callback <c>itemDropped</c>, e não pelo retorno de
    /// <c>TryWrite</c>: com <see cref="BoundedChannelFullMode.DropWrite"/> a escrita é
    /// considerada bem-sucedida mesmo quando o item é jogado fora, então checar o retorno
    /// jamais detectaria um descarte.
    /// </remarks>
    public FilaDeEventos()
    {
        var opcoes = new BoundedChannelOptions(CapacidadeMaxima)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        };

        _canal = Channel.CreateBounded<Evento>(opcoes, _ => Interlocked.Increment(ref _descartados));
    }

    /// <summary>Lê quantos eventos foram descartados por fila cheia e zera o contador.</summary>
    public int DescartadosEZerar() => Interlocked.Exchange(ref _descartados, 0);

    /// <inheritdoc />
    public void Registrar(Evento evento) => _canal.Writer.TryWrite(evento);

    /// <summary>
    /// Lê um lote, esperando pelo primeiro evento e depois drenando o que já estiver disponível.
    /// </summary>
    /// <remarks>
    /// Bloqueia enquanto a fila está vazia — sem laço de espera ativa — e, assim que chega algo,
    /// leva junto tudo o que já se acumulou. Em pico isso vira uma gravação de várias centenas
    /// de linhas; em marcha lenta, uma gravação de uma linha só.
    /// </remarks>
    /// <param name="tamanhoMaximo">Teto de eventos por lote.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public async Task<IReadOnlyList<Evento>> LerLoteAsync(int tamanhoMaximo, CancellationToken ct)
    {
        if (!await _canal.Reader.WaitToReadAsync(ct))
            return [];

        var lote = new List<Evento>(tamanhoMaximo);

        while (lote.Count < tamanhoMaximo && _canal.Reader.TryRead(out var evento))
            lote.Add(evento);

        return lote;
    }
}
