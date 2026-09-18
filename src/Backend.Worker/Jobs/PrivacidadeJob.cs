using Backend.Business.Privacidade.Interfaces;
using Backend.Business.Privacidade.Models;

namespace Backend.Worker.Jobs;

/// <summary>
/// Atende o que os titulares pediram: gera as exportações e executa as eliminações vencidas.
/// </summary>
/// <remarks>
/// Mesma forma dos demais jobs — escopo por execução, roda ao subir, engole a exceção e segue. A
/// regra está em <see cref="IProcessamentoDePrivacidadeService"/>; aqui ficam o relógio, o log e o
/// escopo.
/// <para>
/// <b>Sem <c>FormaturaDoProcessamento</c></b>, e é a diferença para o job de relatórios: titular é
/// pessoa e atravessa turmas, então apontar o escopo para uma formatura esconderia metade dos dados
/// dele. O isolamento é substituído pelo recorte por <c>usuarioId</c>, que vem gravado na própria
/// solicitação.
/// </para>
/// <para>
/// Um escopo por solicitação, e não um para o lote: a eliminação escreve na conta do Identity e em
/// todos os cadastros da pessoa, e um <c>DbContext</c> compartilhado carregaria para a segunda
/// solicitação o rastreador já povoado pela primeira.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class PrivacidadeJob(IServiceScopeFactory scopeFactory, ILogger<PrivacidadeJob> logger) : BackgroundService
{
    /// <summary>
    /// De quanto em quanto tempo a fila é olhada.
    /// </summary>
    /// <remarks>
    /// Meio minuto, e não cinco segundos como a fila de relatórios: ninguém está com a tela aberta
    /// esperando — a exportação avisa por e-mail, e a eliminação tem quinze dias de prazo. Uma
    /// passada vazia é uma consulta a um índice parcial que, na esmagadora maioria dos dias, tem
    /// zero linhas.
    /// </remarks>
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(30);

    /// <summary>Solicitações por passada. Cada uma abre o próprio escopo.</summary>
    private const int TamanhoDoLote = 5;

    /// <summary>De quantas em quantas passadas a limpeza dos pacotes vencidos roda.</summary>
    /// <remarks>Uma vez por hora: a validade é de dias, e varrer o índice junto com a fila não adianta nada.</remarks>
    private static readonly int PassadasEntreLimpezas = (int)(TimeSpan.FromHours(1) / Intervalo);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var relogio = new PeriodicTimer(Intervalo);

        var passada = 0;

        do
        {
            await ExecutarUmaVez(passada % PassadasEntreLimpezas == 0, stoppingToken);

            passada++;
        } while (await EsperarProximaExecucao(relogio, stoppingToken));
    }

    private async Task ExecutarUmaVez(bool limpar, CancellationToken ct)
    {
        try
        {
            foreach (var pendente in await ListarPendentes(ct))
                await Processar(pendente, ct);

            if (limpar)
                await Limpar(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            logger.LogError(excecao, "Falha no processamento de privacidade. A próxima execução tentará de novo.");
        }
    }

    private async Task<IReadOnlyList<PrivacidadePendente>> ListarPendentes(CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        return await escopo.ServiceProvider.GetRequiredService<IProcessamentoDePrivacidadeService>().ListarPendentes(TamanhoDoLote, ct);
    }

    private async Task Processar(PrivacidadePendente pendente, CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        var atendida = await escopo.ServiceProvider.GetRequiredService<IProcessamentoDePrivacidadeService>().Processar(pendente.SolicitacaoId, ct);

        logger.LogInformation(
            "Solicitação de privacidade {SolicitacaoId} ({Tipo}): {Resultado}.",
            pendente.SolicitacaoId,
            pendente.Tipo,
            atendida ? "atendida" : "não atendida"
        );
    }

    private async Task Limpar(CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        var expirados = await escopo.ServiceProvider.GetRequiredService<IProcessamentoDePrivacidadeService>().ExpirarVencidas(ct);

        if (expirados > 0)
            logger.LogInformation("Limpeza de privacidade removeu {Expirados} pacotes vencidos.", expirados);
    }

    private static async Task<bool> EsperarProximaExecucao(PeriodicTimer relogio, CancellationToken ct)
    {
        try
        {
            return await relogio.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
