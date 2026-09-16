using Backend.Business.Abstractions;
using Backend.Business.Relatorios.Interfaces;

namespace Backend.Worker.Jobs;

/// <summary>
/// De poucos em poucos segundos: gera os relatórios pedidos e apaga os que passaram do prazo.
/// </summary>
/// <remarks>
/// Mesma forma dos demais jobs — escopo por execução, roda ao subir, engole a exceção e segue. A
/// regra de gerar está em <see cref="IGeracaoDeRelatoriosService"/>; aqui ficam o relógio, o log e o
/// escopo.
/// <para>
/// <b>Um escopo por solicitação</b>, apontado para a formatura dela. É o que faz o filtro global
/// continuar valendo dentro do worker: sem isso, o balancete sairia vazio — ou, pior, o mesmo
/// <c>DbContext</c> atenderia duas turmas seguidas com o cache da primeira já povoado.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class GeracaoDeRelatoriosJob(IServiceScopeFactory scopeFactory, ILogger<GeracaoDeRelatoriosJob> logger) : BackgroundService
{
    /// <summary>
    /// De quantos em quantos segundos a fila é olhada.
    /// </summary>
    /// <remarks>
    /// Curto de propósito, e agora de verdade: quem pediu o PDF está com a tela aberta esperando o
    /// arquivo baixar sozinho, e um minuto de espera com o aviso girando parece travamento — era a
    /// reclamação de que "o toast só fica gerando". Uma passada vazia é uma consulta a um índice
    /// parcial de cinco linhas, não um trabalho.
    /// </remarks>
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(5);

    /// <summary>Solicitações por passada. Cada uma abre o próprio escopo.</summary>
    private const int TamanhoDoLote = 5;

    /// <summary>De quantas em quantas passadas a limpeza dos vencidos roda.</summary>
    /// <remarks>Uma vez por hora: expiração é de dias, e varrer o índice junto com a fila não adianta nada.</remarks>
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
            var pendentes = await ListarPendentes(ct);

            foreach (var pendente in pendentes)
                await Gerar(pendente, ct);

            if (limpar)
                await Limpar(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            logger.LogError(excecao, "Falha na geração de relatórios. A próxima execução tentará de novo.");
        }
    }

    private async Task<IReadOnlyList<RelatorioPendente>> ListarPendentes(CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        return await escopo.ServiceProvider.GetRequiredService<IGeracaoDeRelatoriosService>().ListarPendentes(TamanhoDoLote, ct);
    }

    /// <summary>Gera uma solicitação em escopo próprio, apontado para a formatura dela.</summary>
    /// <param name="pendente">Solicitação e a turma a que ela pertence.</param>
    private async Task Gerar(RelatorioPendente pendente, CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        escopo.ServiceProvider.GetRequiredService<FormaturaDoProcessamento>().Apontar(pendente.FormaturaId);

        var gerado = await escopo.ServiceProvider.GetRequiredService<IGeracaoDeRelatoriosService>().Gerar(pendente.SolicitacaoId, ct);

        logger.LogInformation(
            "Relatório {SolicitacaoId} da formatura {FormaturaId}: {Resultado}.",
            pendente.SolicitacaoId,
            pendente.FormaturaId,
            gerado ? "gerado" : "não gerado"
        );
    }

    private async Task Limpar(CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        var expirados = await escopo.ServiceProvider.GetRequiredService<IGeracaoDeRelatoriosService>().ExpirarVencidas(ct);

        if (expirados > 0)
            logger.LogInformation("Limpeza de relatórios removeu {Expirados} arquivos vencidos.", expirados);
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
