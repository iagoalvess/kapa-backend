using Backend.Worker.Configuration;

namespace Backend.Worker.Jobs;

/// <summary>
/// O laço que todo job repete: roda ao subir, depois a cada <see cref="Intervalo"/>, engole a exceção e
/// segue.
/// </summary>
/// <remarks>
/// Três coisas que cada job fazia na mão, e que agora moram aqui:
/// <list type="bullet">
/// <item>roda uma vez ao subir e depois no intervalo, para não esperar horas até a primeira execução
/// em um container que reinicia;</item>
/// <item>assume a <see cref="LiderancaDeJob"/> antes da passada, quando o job a recebe — quem não pega
/// pula a passada;</item>
/// <item>engole a exceção e segue, porque falha em uma execução não pode derrubar o host — o que
/// mataria também os outros jobs. <see cref="OperationCanceledException"/> de desligamento é
/// reerguida.</item>
/// </list>
/// <para>
/// O escopo por execução continua sendo do job: só ele sabe se a passada abre um escopo, um por turma
/// ou um por solicitação.
/// </para>
/// </remarks>
/// <param name="lideranca">Trava de réplica única; <c>null</c> para o job que pode rodar em todas.</param>
/// <param name="logger">Log estruturado do job.</param>
public abstract class JobPeriodico(LiderancaDeJob? lideranca, ILogger logger) : BackgroundService
{
    /// <summary>Log estruturado do job.</summary>
    protected ILogger Logger { get; } = logger;

    /// <summary>De quanto em quanto tempo a passada roda.</summary>
    protected abstract TimeSpan Intervalo { get; }

    /// <summary>Registra no log a falha de uma passada — cada job com a sua mensagem.</summary>
    /// <param name="excecao">O que deu errado.</param>
    protected abstract void RegistrarFalha(Exception excecao);

    /// <summary>Faz o trabalho de uma passada.</summary>
    /// <param name="passada">Quantas passadas já rodaram desde a subida — a primeira é zero.</param>
    protected abstract Task ExecutarPassada(int passada, CancellationToken ct);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var relogio = new PeriodicTimer(Intervalo);

        var passada = 0;

        do
        {
            await ExecutarUmaVez(passada++, stoppingToken);
        } while (await EsperarProximaExecucao(relogio, stoppingToken));
    }

    private async Task ExecutarUmaVez(int passada, CancellationToken ct)
    {
        try
        {
            if (lideranca is null)
            {
                await ExecutarPassada(passada, ct);
                return;
            }

            await using var lider = await lideranca.Assumir(GetType().Name, ct);
            if (lider is null)
                return;

            await ExecutarPassada(passada, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            RegistrarFalha(excecao);
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
