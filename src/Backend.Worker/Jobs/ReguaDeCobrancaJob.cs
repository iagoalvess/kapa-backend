using Backend.Business.Abstractions;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;
using Backend.Worker.Configuration;

namespace Backend.Worker.Jobs;

/// <summary>
/// De hora em hora: roda a régua de cobrança de cada turma ativa, dentro da janela de 9h às 20h.
/// </summary>
/// <remarks>
/// Mesma forma dos demais jobs — escopo por execução, roda ao subir, engole a exceção e segue. A
/// regra toda está em <see cref="IReguaService"/>; aqui ficam o relógio, o log e o escopo.
/// <para>
/// <b>Um escopo por formatura</b>, apontado para ela antes da primeira consulta: é o que faz o filtro
/// global continuar valendo dentro do worker. Sem isso a régua não enxergaria parcela nenhuma — ou,
/// pior, o mesmo <c>DbContext</c> atenderia duas turmas seguidas com o cache da primeira já povoado.
/// </para>
/// <para>
/// De hora em hora, e não uma vez às 9h: assim uma turma não perde o dia inteiro porque o worker
/// estava reiniciando às 9h em ponto. Rodar de novo não duplica nada — quem garante isso é o índice
/// único <c>(parcela, regra, dia, destinatário)</c>, e não o relógio.
/// </para>
/// <para>
/// Quem confere a janela é o <see cref="IReguaService"/>, e não este laço: fora dela a rodada ainda
/// fecha o histórico com o desfecho dos e-mails já enviados, sem falar com ninguém.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="lideranca">Trava que deixa só uma réplica rodar este job por vez.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ReguaDeCobrancaJob(IServiceScopeFactory scopeFactory, LiderancaDeJob lideranca, ILogger<ReguaDeCobrancaJob> logger)
    : JobPeriodico(lideranca, logger)
{
    /// <inheritdoc />
    protected override TimeSpan Intervalo { get; } = TimeSpan.FromHours(1);

    /// <inheritdoc />
    protected override void RegistrarFalha(Exception excecao) =>
        Logger.LogError(excecao, "Falha na régua de cobrança. A próxima execução tentará de novo.");

    /// <inheritdoc />
    protected override async Task ExecutarPassada(int passada, CancellationToken ct)
    {
        var agora = DateTime.UtcNow;

        foreach (var formatura in await ListarFormaturas(ct))
            await Rodar(formatura, agora, ct);
    }

    private async Task<IReadOnlyList<FormaturaParaRegua>> ListarFormaturas(CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        return await escopo.ServiceProvider.GetRequiredService<IReguaService>().ListarFormaturas(ct);
    }

    /// <summary>Roda a régua de uma turma em escopo próprio, apontado para ela.</summary>
    /// <param name="formatura">Turma e nome.</param>
    /// <param name="agoraUtc">Momento da rodada.</param>
    private async Task Rodar(FormaturaParaRegua formatura, DateTime agoraUtc, CancellationToken ct)
    {
        try
        {
            using var escopo = scopeFactory.CreateScope();

            escopo.ServiceProvider.GetRequiredService<FormaturaDoProcessamento>().Apontar(formatura.Id);

            var resumo = await escopo.ServiceProvider.GetRequiredService<IReguaService>().Executar(formatura, agoraUtc, ct);

            if (!resumo.Vazia)
                Logger.LogInformation(
                    "Régua da formatura {FormaturaId}: {Mensagens} mensagens, {Parcelas} parcelas, {Conferidas} entregas conferidas.",
                    formatura.Id,
                    resumo.Mensagens,
                    resumo.Parcelas,
                    resumo.Conferidas
                );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            Logger.LogError(excecao, "Falha na régua da formatura {FormaturaId}. As demais turmas seguem.", formatura.Id);
        }
    }
}
