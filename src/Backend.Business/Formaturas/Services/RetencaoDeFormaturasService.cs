using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Formaturas.Services;

/// <summary>
/// O fim da vida de uma turma.
/// </summary>
/// <param name="retencaoRepository">Consultas e remoção em massa.</param>
/// <param name="formaturaRepository">A turma, rastreada.</param>
/// <param name="armazenamento">Provedor dos arquivos.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class RetencaoDeFormaturasService(
    IRetencaoDeFormaturasRepository retencaoRepository,
    IFormaturaRepository formaturaRepository,
    IArmazenamentoDeArquivos armazenamento,
    IUnitOfWork unitOfWork,
    ILogger<RetencaoDeFormaturasService> logger
) : IRetencaoDeFormaturasService
{
    /// <summary>Quanto tempo a turma fica suspensa antes de encerrar sozinha.</summary>
    public static readonly TimeSpan SuspensaAteEncerrar = TimeSpan.FromDays(365);

    /// <summary>Quanto tempo a encerrada fica guardada.</summary>
    public static readonly TimeSpan GuardaDaEncerrada = TimeSpan.FromDays(5 * 365 + 1);

    /// <summary>Quanto tempo a descartada fica guardada, para desfazer um descarte por engano.</summary>
    public static readonly TimeSpan GuardaDaDescartada = TimeSpan.FromDays(30);

    /// <summary>Turmas por passada: o job roda todo dia, e o que sobrar fica para o seguinte.</summary>
    private const int Lote = 50;

    /// <inheritdoc />
    public async Task<int> EncerrarSuspensasAbandonadas(CancellationToken ct = default)
    {
        var abandonadas = await retencaoRepository.ListarSuspensasAnterioresA(DateTime.UtcNow - SuspensaAteEncerrar, Lote, ct);

        foreach (var formatura in abandonadas)
            formatura.Transicionar(StatusDaFormatura.Encerrada);

        if (abandonadas.Count > 0)
            await unitOfWork.SalvarAsync(ct);

        return abandonadas.Count;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> ListarParaEliminar(CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;

        return retencaoRepository.ListarParaEliminar(agora - GuardaDaEncerrada, agora - GuardaDaDescartada, Lote, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Os arquivos saem <b>antes</b> das linhas. Se o banco falhar depois, a turma continua não eliminada e
    /// a próxima passada repete tudo — apagar um prefixo já vazio não custa nada. Na ordem inversa, uma falha
    /// no provedor deixaria bytes que nenhuma linha aponta mais, e ninguém voltaria para buscá-los.
    /// </remarks>
    public async Task<bool> Eliminar(Guid formaturaId, CancellationToken ct = default)
    {
        var formatura = await formaturaRepository.ObterParaEdicao(formaturaId, ct);

        if (formatura is null || !Vencida(formatura, DateTime.UtcNow))
            return false;

        var prefixo = ArquivoService.PrefixoDaFormatura(formaturaId);

        await armazenamento.RemoverPrefixoAsync(prefixo, ct);

        await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                await retencaoRepository.ApagarDados(formaturaId, prefixo, token);
                formatura.MarcarEliminada();

                return Result.Ok();
            },
            ct
        );

        logger.LogWarning("Formatura {FormaturaId} ({Status}) eliminada pela retenção.", formaturaId, formatura.Status);

        return true;
    }

    /// <summary>A mesma regra da consulta, conferida de novo na linha rastreada antes de apagar.</summary>
    private static bool Vencida(Formatura formatura, DateTime agora) =>
        formatura.EliminadaEm is null
        && formatura.Status switch
        {
            StatusDaFormatura.Encerrada => formatura.EncerradaEm < agora - GuardaDaEncerrada,
            StatusDaFormatura.Descartada => formatura.StatusDesde < agora - GuardaDaDescartada,
            _ => false,
        };
}
