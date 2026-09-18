using Backend.Business.Eventos.Interfaces;

namespace Backend.Worker.Jobs;

/// <summary>
/// Apaga eventos mais antigos que o prazo de retenção.
/// </summary>
/// <remarks>
/// A tabela de eventos registra atividade, não estado: ela só cresce, e cresce proporcional ao
/// uso. Sem retenção, é a primeira a encher o disco — e a que mais lentifica as consultas do
/// painel administrativo no caminho.
/// <para>
/// O prazo vem de <c>Eventos:DiasDeRetencao</c>. Antes de encurtá-lo, confira se alguém depende
/// de comparação ano a ano: 180 dias não respondem "cresceu em relação ao mesmo mês do ano
/// passado".
/// </para>
/// <para>
/// <b>Dois prazos desde a Sprint 14.</b> O evento de uso obedece a <c>Eventos:DiasDeRetencao</c>; os
/// nomes de <c>NomesDeAuditoria</c> obedecem a <c>Eventos:DiasDeRetencaoAuditoria</c>, que é de
/// cinco anos. Uma turma leva de dois a quatro anos e a prestação de contas é no fim dela — com um
/// prazo só, a assembleia do terceiro ano pergunta "quem baixou esta parcela?" e a resposta é que o
/// evento já foi apagado. Cinco anos só para o que é auditável mantém barata a tabela que mais
/// cresce.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="configuration">Configuração da aplicação.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class RetencaoDeEventosJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<RetencaoDeEventosJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(24);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var diasDeRetencao = configuration.GetValue("Eventos:DiasDeRetencao", 180);
        var diasDaAuditoria = Math.Max(diasDeRetencao, configuration.GetValue("Eventos:DiasDeRetencaoAuditoria", 1825));

        using var relogio = new PeriodicTimer(Intervalo);

        do
        {
            await ExecutarUmaVez(diasDeRetencao, diasDaAuditoria, stoppingToken);
        } while (await EsperarProximaExecucao(relogio, stoppingToken));
    }

    private async Task ExecutarUmaVez(int diasDeRetencao, int diasDaAuditoria, CancellationToken ct)
    {
        try
        {
            using var escopo = scopeFactory.CreateScope();
            var repositorio = escopo.ServiceProvider.GetRequiredService<IEventoRepository>();

            var agora = DateTime.UtcNow;

            var removidos = await repositorio.RemoverAnterioresA(agora.AddDays(-diasDeRetencao), agora.AddDays(-diasDaAuditoria), ct);

            if (removidos > 0)
                logger.LogInformation(
                    "Retenção removeu {Removidos} eventos: {Dias} dias de uso, {DiasDaAuditoria} dias de auditoria.",
                    removidos,
                    diasDeRetencao,
                    diasDaAuditoria
                );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            logger.LogError(excecao, "Falha na retenção de eventos. A próxima execução tentará de novo.");
        }
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
