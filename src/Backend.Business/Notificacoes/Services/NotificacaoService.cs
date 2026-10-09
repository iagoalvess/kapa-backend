using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Common.Datas;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;
using Backend.Business.Pagamentos.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business.Notificacoes.Services;

/// <summary>
/// A régua como a comissão a governa: os degraus, o histórico e o disparo avulso.
/// </summary>
/// <param name="notificacoes">Régua, histórico e seleção de parcelas.</param>
/// <param name="parcelas">Regras de atraso aceitas na adesão, e se a parcela existe na turma.</param>
/// <param name="formaturas">Nome da turma, que vai na variável <c>{formatura}</c>.</param>
/// <param name="canal">Por onde a mensagem sai.</param>
/// <param name="aplicacao">Identidade da aplicação, para os links.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class NotificacaoService(
    INotificacaoRepository notificacoes,
    IParcelaRepository parcelas,
    IFormaturaRepository formaturas,
    ICanalDeNotificacao canal,
    IOptions<AplicacaoSettings> aplicacao,
    IUnitOfWork unitOfWork,
    ILogger<NotificacaoService> logger
) : INotificacaoService
{
    private static readonly Erro RegraNaoEncontrada = Erro.NaoEncontrado("notificacao.regra_nao_encontrada", "Degrau da régua não encontrado.");

    private static readonly Erro ParcelaNaoCobravel = Erro.Conflito(
        "notificacao.parcela_nao_cobravel",
        "Esta parcela não pode ser cobrada agora: ela já foi paga, foi cancelada ou tem um aviso de pagamento esperando conferência."
    );

    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RegraResumo>>> ListarRegras(CancellationToken ct = default) =>
        Result.Ok(await ReguaDaTurma.Garantir(notificacoes, unitOfWork, ct));

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RegraResumo>>> DefinirRegra(Guid regraId, bool ativa, CancellationToken ct = default)
    {
        if (await notificacoes.ObterRegraParaEdicao(regraId, ct) is not { } regra || ReguaDoKapa.De(regra.Gatilho, regra.DiasDeDeslocamento) is null)
            return Result.Falha<IReadOnlyList<RegraResumo>>(RegraNaoEncontrada);

        regra.Definir(ativa);
        await unitOfWork.SalvarAsync(ct);

        return Result.Ok(await ReguaDaTurma.Garantir(notificacoes, unitOfWork, ct));
    }

    /// <inheritdoc />
    public async Task<Result<PaginaDe<NotificacaoNoHistorico>>> ListarHistorico(
        PaginacaoRequest paginacao,
        FiltroDeNotificacoes filtro,
        CancellationToken ct = default
    ) => Result.Ok(await notificacoes.ListarHistorico(paginacao.Normalizar(), filtro, ct));

    /// <inheritdoc />
    public async Task<Result> Cobrar(Guid formaturaId, Guid parcelaId, CancellationToken ct = default)
    {
        if (await notificacoes.ObterParaCobranca(parcelaId, ct) is not { } parcela)
            return await parcelas.Obter(parcelaId, DataUtils.Hoje(), ct) is null
                ? Result.Falha(ErrosDePagamento.ParcelaNaoEncontrada)
                : Result.Falha(ParcelaNaoCobravel);

        var regras = await ReguaDaTurma.Garantir(notificacoes, unitOfWork, ct);
        var hoje = JanelaDeEnvio.Hoje(DateTime.UtcNow);

        if (Avulsa(regras, parcela, hoje) is not { } regra)
            return Result.Falha(Erro.Conflito("notificacao.sem_degrau", "A régua não tem nenhum degrau ativo para usar nesta cobrança."));

        var ja = await notificacoes.ListarChavesDoDia(hoje, ct);
        if (ja.Contains(new ChaveDeEnvio(regra.Id, parcelaId)))
            return Result.Falha(Erro.Conflito("notificacao.ja_cobrada_hoje", "Esta parcela já foi cobrada hoje."));

        var atraso = (await parcelas.ObterRegrasDeAtraso([parcela.VinculoId], ct)).GetValueOrDefault(parcela.VinculoId, RegrasDeAtraso.Nenhuma);
        var valor = ValorDoDia.Calcular(parcela.ValorOriginalEmCentavos, parcela.Vencimento, hoje, atraso, parcela.JaPagoEmCentavos);

        var mensagem = MontagemDaMensagem.Cobranca(
            regra,
            parcela.Nome,
            parcela.Email,
            await NomeDaFormatura(formaturaId, ct),
            [new ParcelaNaMensagem(parcela, valor)],
            MontagemDaMensagem.LinkDoExtrato(_aplicacao)
        );

        var enviado = await canal.Enviar(mensagem, ct);
        if (enviado.Falhou)
            return Result.Falha(enviado.Erros);

        await notificacoes.AdicionarEnvios(
            [
                NotificacaoEnviada.Nova(
                    regra.Id,
                    hoje,
                    parcela.Email,
                    mensagem.Assunto,
                    parcela.ParcelaId,
                    parcela.VinculoId,
                    enviado.Valor.ReferenciaExterna
                ),
            ],
            ct
        );
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Cobrança avulsa da parcela {ParcelaId} pelo degrau {RegraId}.", parcelaId, regra.Id);

        return Result.Ok();
    }

    /// <summary>
    /// O degrau que o disparo avulso usa: o de atraso mais próximo do atraso real da parcela.
    /// </summary>
    /// <remarks>
    /// Sem degrau de atraso ligado, cai no de vencimento com o maior deslocamento — o texto mais
    /// severo da régua. Sem nenhum de vencimento ativo, não há o que mandar.
    /// </remarks>
    private static RegraResumo? Avulsa(IReadOnlyList<RegraResumo> regras, ParcelaParaCobranca parcela, DateOnly hoje)
    {
        var atraso = hoje.DayNumber - parcela.Vencimento.DayNumber;

        var candidatas = regras.Where(r => r.Ativa && r.Gatilho is GatilhoDaRegua.Vencimento).ToList();

        return candidatas.Where(r => r.DiasDeDeslocamento <= atraso).MaxBy(r => r.DiasDeDeslocamento) ?? candidatas.MinBy(r => r.DiasDeDeslocamento);
    }

    private async Task<string> NomeDaFormatura(Guid formaturaId, CancellationToken ct) =>
        await formaturas.ObterNome(formaturaId, ct) ?? _aplicacao.Nome;
}
