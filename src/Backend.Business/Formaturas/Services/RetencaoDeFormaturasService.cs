using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Services;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
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
/// <param name="pendencias">O que ficou aberto na turma encerrada por abandono (Sprint 42, decisão 7).</param>
/// <param name="eventos">Auditoria do encerramento por abandono.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class RetencaoDeFormaturasService(
    IRetencaoDeFormaturasRepository retencaoRepository,
    IFormaturaRepository formaturaRepository,
    IArmazenamentoDeArquivos armazenamento,
    IPendenciasDaTurmaRepository pendencias,
    IEventoRepository eventos,
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
    public Task<IReadOnlyList<Guid>> ListarSuspensasAbandonadas(CancellationToken ct = default) =>
        retencaoRepository.ListarSuspensasAnterioresA(DateTime.UtcNow - SuspensaAteEncerrar, Lote, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Encerra mesmo com parcela aberta, aviso pendente ou compra a devolver: um ano suspensa é uma turma que ninguém
    /// vai voltar a administrar. O que muda é o registro — sem ele, quem aparecer depois perguntando pelo dinheiro não
    /// tem resposta. O evento não tem autor: foi a retenção.
    /// </remarks>
    public async Task<bool> EncerrarAbandonada(Guid formaturaId, CancellationToken ct = default)
    {
        var formatura = await formaturaRepository.ObterParaEdicao(formaturaId, ct);
        var agora = DateTime.UtcNow;

        if (formatura is not { Status: StatusDaFormatura.Suspensa } || formatura.StatusDesde >= agora - SuspensaAteEncerrar)
            return false;

        var emAberto = await pendencias.ContarParaEncerrar(agora, ct);

        formatura.Transicionar(StatusDaFormatura.Encerrada);

        await eventos.Auditar(
            NomesDeAuditoria.EncerradaPorAbandono,
            null,
            new
            {
                formaturaId,
                suspensaDesde = formatura.StatusDesde,
                pendencias = emAberto,
                emAberto = emAberto.Descrever(),
            },
            ct
        );

        await unitOfWork.SalvarAsync(ct);

        if (emAberto.Alguma)
            logger.LogWarning("Formatura {FormaturaId} encerrada por abandono com pendências: {Pendencias}.", formaturaId, emAberto.Descrever());

        return true;
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
