using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Interfaces;
using Backend.Business.Adesoes.Models;
using Backend.Worker.Configuration;

namespace Backend.Worker.Jobs;

/// <summary>
/// De 15 em 15 minutos: resume por IA as versões do termo publicadas na última semana que ainda não
/// têm resumo (Sprint 24).
/// </summary>
/// <remarks>
/// Mesma forma dos demais jobs. A regra — janela de sete dias, teto por rodada, descarte — está em
/// <see cref="IResumoDoTermoService"/>; aqui ficam o relógio, o log e o escopo.
/// <para>
/// A lista atravessa turmas, mas <b>cada geração roda em escopo próprio, apontado para a turma do
/// termo</b> antes da primeira consulta, como no <see cref="ReguaDeCobrancaJob"/>: o filtro global
/// continua valendo, e nenhum <c>DbContext</c> atende duas turmas seguidas.
/// </para>
/// <para>
/// Quinze minutos porque a consulta quase sempre volta vazia, e uma hora seria esperar uma hora para
/// conferir o próprio trabalho. Falhou, loga e a próxima rodada tenta de novo. Com a chave vazia a
/// lista vem vazia sem tocar no banco, e o job não escreve nada no log.
/// </para>
/// </remarks>
/// <param name="scopeFactory">Fábrica de escopos de injeção de dependência.</param>
/// <param name="lideranca">Trava que deixa só uma réplica rodar este job por vez — duas gastariam cota em dobro.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class ResumoDoTermoJob(IServiceScopeFactory scopeFactory, LiderancaDeJob lideranca, ILogger<ResumoDoTermoJob> logger)
    : JobPeriodico(lideranca, logger)
{
    /// <inheritdoc />
    protected override TimeSpan Intervalo { get; } = TimeSpan.FromMinutes(15);

    /// <inheritdoc />
    protected override void RegistrarFalha(Exception excecao) =>
        Logger.LogError(excecao, "Falha no resumo do termo. A próxima execução tentará de novo.");

    /// <inheritdoc />
    protected override async Task ExecutarPassada(int passada, CancellationToken ct)
    {
        foreach (var termo in await ListarPendentes(ct))
            await Gerar(termo, ct);
    }

    private async Task<IReadOnlyList<TermoSemResumo>> ListarPendentes(CancellationToken ct)
    {
        using var escopo = scopeFactory.CreateScope();

        return await escopo.ServiceProvider.GetRequiredService<IResumoDoTermoService>().ListarPendentes(DateTime.UtcNow, ct);
    }

    /// <summary>Gera o resumo de uma versão em escopo próprio, apontado para a turma dela.</summary>
    /// <param name="termo">Versão e turma.</param>
    private async Task Gerar(TermoSemResumo termo, CancellationToken ct)
    {
        try
        {
            using var escopo = scopeFactory.CreateScope();

            escopo.ServiceProvider.GetRequiredService<FormaturaDoProcessamento>().Apontar(termo.FormaturaId);

            var resultado = await escopo.ServiceProvider.GetRequiredService<IResumoDoTermoService>().Gerar(termo.TermoId, ct);

            if (resultado.Falhou)
                Logger.LogWarning(
                    "Resumo do termo {TermoId} da formatura {FormaturaId} não gravado: {Motivo}",
                    termo.TermoId,
                    termo.FormaturaId,
                    resultado.PrimeiroErro.Mensagem
                );
            else
                Logger.LogInformation("Resumo do termo {TermoId} da formatura {FormaturaId} gravado.", termo.TermoId, termo.FormaturaId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            Logger.LogError(excecao, "Falha no resumo do termo {TermoId}. Os demais seguem.", termo.TermoId);
        }
    }
}
