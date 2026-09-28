using Backend.Business.Abstractions;
using Backend.Business.Formaturas.Interfaces;
using Backend.Worker.Configuration;

namespace Backend.Worker.Jobs;

/// <summary>
/// Uma vez por dia: encerra a turma suspensa há 12 meses e elimina a que passou do prazo de guarda.
/// </summary>
/// <remarks>
/// Mesma forma dos demais jobs — escopo por execução, roda ao subir, engole a exceção e segue. Os prazos
/// e a regra estão em <see cref="IRetencaoDeFormaturasService"/>; aqui ficam o relógio, o log e o escopo.
/// <para>
/// <b>Um escopo por turma</b>, apontado para ela, como no job de relatórios: é o filtro global que
/// garante que a remoção só alcança as linhas da turma. Turma que falha não segura as outras — o erro
/// vai para o log, e ela volta na lista de amanhã.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos.</param>
/// <param name="lideranca">Uma réplica por vez.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class RetencaoDeFormaturasJob(IServiceScopeFactory scopeFactory, LiderancaDeJob lideranca, ILogger<RetencaoDeFormaturasJob> logger)
    : JobPeriodico(lideranca, logger)
{
    /// <inheritdoc />
    protected override TimeSpan Intervalo { get; } = TimeSpan.FromHours(24);

    /// <inheritdoc />
    protected override void RegistrarFalha(Exception excecao) =>
        Logger.LogError(excecao, "Falha na retenção de turmas. A próxima execução tentará de novo.");

    /// <inheritdoc />
    protected override async Task ExecutarPassada(int passada, CancellationToken ct)
    {
        IReadOnlyList<Guid> vencidas;

        using (var escopo = scopeFactory.CreateScope())
        {
            var retencao = escopo.ServiceProvider.GetRequiredService<IRetencaoDeFormaturasService>();

            var encerradas = await retencao.EncerrarSuspensasAbandonadas(ct);

            if (encerradas > 0)
                Logger.LogInformation("Retenção encerrou {Encerradas} turmas suspensas há mais de 12 meses.", encerradas);

            vencidas = await retencao.ListarParaEliminar(ct);
        }

        foreach (var formaturaId in vencidas)
            await Eliminar(formaturaId, ct);
    }

    private async Task Eliminar(Guid formaturaId, CancellationToken ct)
    {
        try
        {
            using var escopo = scopeFactory.CreateScope();

            escopo.ServiceProvider.GetRequiredService<FormaturaDoProcessamento>().Apontar(formaturaId);

            await escopo.ServiceProvider.GetRequiredService<IRetencaoDeFormaturasService>().Eliminar(formaturaId, ct);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Logger.LogError(excecao, "Falha ao eliminar a formatura {FormaturaId}. Ela volta na próxima execução.", formaturaId);
        }
    }
}
