using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Notificacoes.Interfaces;
using Backend.Business.Notificacoes.Models;
using Backend.Business.Usuarios.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business.Notificacoes.Services;

/// <summary>
/// A régua como a comissão a governa: os degraus, o teste, o histórico, as preferências e o disparo avulso.
/// </summary>
/// <param name="notificacoes">Régua, histórico e seleção de parcelas.</param>
/// <param name="parcelas">Regras de atraso aceitas na adesão.</param>
/// <param name="vinculos">Vínculo de quem chama.</param>
/// <param name="formaturas">Nome da turma, que vai na variável <c>{formatura}</c>.</param>
/// <param name="usuarios">E-mail e nome de quem clicou em "testar".</param>
/// <param name="canal">Por onde a mensagem sai.</param>
/// <param name="validadorDaRegua">Forma da régua.</param>
/// <param name="validadorDasPreferencias">Forma das preferências.</param>
/// <param name="aplicacao">Identidade da aplicação, para os links.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class NotificacaoService(
    INotificacaoRepository notificacoes,
    IParcelaRepository parcelas,
    IVinculoRepository vinculos,
    IFormaturaRepository formaturas,
    IUsuarioRepository usuarios,
    ICanalDeNotificacao canal,
    IValidator<DadosDaRegua> validadorDaRegua,
    IValidator<DadosDasPreferencias> validadorDasPreferencias,
    IOptions<AplicacaoSettings> aplicacao,
    IUnitOfWork unitOfWork,
    ILogger<NotificacaoService> logger
) : INotificacaoService
{
    private static readonly Erro RegraNaoEncontrada = Erro.NaoEncontrado("notificacao.regra_nao_encontrada", "Degrau da régua não encontrado.");

    private static readonly Erro CobrancaObrigatoria = Erro.Conflito(
        "notificacao.cobranca_obrigatoria",
        "O aviso de parcela é comunicação do termo de adesão e não pode ser desligado."
    );

    private static readonly Erro ParcelaNaoCobravel = Erro.Conflito(
        "notificacao.parcela_nao_cobravel",
        "Esta parcela não pode ser cobrada agora: ela já foi paga, foi cancelada ou tem um aviso de pagamento esperando conferência."
    );

    private readonly AplicacaoSettings _aplicacao = aplicacao.Value;

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<RegraResumo>>> ListarRegras(CancellationToken ct = default) =>
        Result.Ok(await ReguaDaTurma.Garantir(notificacoes, unitOfWork, ct));

    /// <inheritdoc />
    /// <remarks>
    /// O par <c>(gatilho, dias)</c> é a identidade do degrau: o que veio no corpo é atualizado, o que
    /// não existia é criado, e o que sumiu é removido. Uma tela, uma transação.
    /// </remarks>
    public async Task<Result<IReadOnlyList<RegraResumo>>> SalvarRegras(DadosDaRegua dados, CancellationToken ct = default)
    {
        var validacao = validadorDaRegua.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<IReadOnlyList<RegraResumo>>(validacao.Erros);

        await ReguaDaTurma.Garantir(notificacoes, unitOfWork, ct);

        var gravadas = await notificacoes.ListarRegrasParaEdicao(ct);
        var porChave = gravadas.ToDictionary(r => (r.Gatilho, r.DiasDeDeslocamento));

        List<RegraDeNotificacao> novas = [];

        foreach (var degrau in dados.Regras)
        {
            if (porChave.TryGetValue((degrau.Gatilho, degrau.DiasDeDeslocamento), out var existente))
                existente.Aplicar(degrau);
            else
                novas.Add(
                    RegraDeNotificacao.Nova(
                        degrau.Gatilho,
                        degrau.DiasDeDeslocamento,
                        degrau.Assunto,
                        degrau.Template,
                        degrau.Ativa,
                        degrau.AvisarTesouraria
                    )
                );
        }

        var chavesEnviadas = dados.Regras.Select(r => (r.Gatilho, r.DiasDeDeslocamento)).ToHashSet();

        await notificacoes.AdicionarRegras(novas, ct);
        notificacoes.RemoverRegras([.. gravadas.Where(r => !chavesEnviadas.Contains((r.Gatilho, r.DiasDeDeslocamento)))]);
        await unitOfWork.SalvarAsync(ct);

        return Result.Ok(await notificacoes.ListarRegras(ct));
    }

    /// <inheritdoc />
    public async Task<Result> Testar(Guid formaturaId, Guid usuarioId, Guid regraId, CancellationToken ct = default)
    {
        var regras = await ReguaDaTurma.Garantir(notificacoes, unitOfWork, ct);

        if (regras.FirstOrDefault(r => r.Id == regraId) is not { } regra)
            return Result.Falha(RegraNaoEncontrada);

        if (await usuarios.ObterDetalhe(usuarioId, ct) is not { } quemClicou)
            return Result.Falha(RegraNaoEncontrada);

        var nome = await NomeDaFormatura(formaturaId, ct);
        var link = $"{_aplicacao.UrlDoFrontend.TrimEnd('/')}/notificacoes/lembretes";

        var enviado = await canal.Enviar(MontagemDaMensagem.Exemplo(regra, quemClicou.Nome, quemClicou.Email, nome, link), ct);
        if (enviado.Falhou)
            return Result.Falha(enviado.Erros);

        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Degrau {RegraId} testado por {UsuarioId} — a mensagem foi só para ele.", regraId, usuarioId);

        return Result.Ok();
    }

    /// <inheritdoc />
    public async Task<Result<PaginaDe<NotificacaoNoHistorico>>> ListarHistorico(
        PaginacaoRequest paginacao,
        FiltroDeNotificacoes filtro,
        CancellationToken ct = default
    ) => Result.Ok(await notificacoes.ListarHistorico(paginacao.Normalizar(), filtro, ct));

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PreferenciaResumo>>> ListarPreferencias(Guid formaturaId, Guid usuarioId, CancellationToken ct = default)
    {
        if (await vinculos.ObterAtivoParaEdicao(usuarioId, formaturaId, ct) is not { } vinculo)
            return Result.Falha<IReadOnlyList<PreferenciaResumo>>(Erro.Proibido("notificacao.sem_vinculo", "Você não participa desta formatura."));

        return Result.Ok(Montar(await notificacoes.ListarPreferenciasParaEdicao(vinculo.Id, ct)));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Decisão 7: lembrete de assembleia e aviso do mural o formando desliga; cobrança de parcela
    /// vencida, não — é comunicação contratual, prevista no termo de adesão.
    /// </remarks>
    public async Task<Result<IReadOnlyList<PreferenciaResumo>>> SalvarPreferencias(
        Guid formaturaId,
        Guid usuarioId,
        DadosDasPreferencias dados,
        CancellationToken ct = default
    )
    {
        var validacao = validadorDasPreferencias.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<IReadOnlyList<PreferenciaResumo>>(validacao.Erros);

        if (dados.Preferencias.Any(p => !p.Ativa && !TiposDeNotificacao.Opcional(p.Tipo)))
            return Result.Falha<IReadOnlyList<PreferenciaResumo>>(CobrancaObrigatoria);

        if (await vinculos.ObterAtivoParaEdicao(usuarioId, formaturaId, ct) is not { } vinculo)
            return Result.Falha<IReadOnlyList<PreferenciaResumo>>(Erro.Proibido("notificacao.sem_vinculo", "Você não participa desta formatura."));

        var gravadas = await notificacoes.ListarPreferenciasParaEdicao(vinculo.Id, ct);
        var porTipo = gravadas.ToDictionary(p => p.Tipo);

        List<PreferenciaDeNotificacao> novas = [];

        foreach (var escolha in dados.Preferencias.Where(p => TiposDeNotificacao.Opcional(p.Tipo)))
        {
            if (porTipo.TryGetValue(escolha.Tipo, out var existente))
                existente.Definir(escolha.Ativa);
            else
                novas.Add(PreferenciaDeNotificacao.Nova(vinculo.Id, escolha.Tipo, escolha.Ativa));
        }

        await notificacoes.AdicionarPreferencias(novas, ct);
        await unitOfWork.SalvarAsync(ct);

        return Result.Ok(Montar([.. gravadas, .. novas]));
    }

    /// <inheritdoc />
    public async Task<Result> Cobrar(Guid formaturaId, Guid parcelaId, CancellationToken ct = default)
    {
        if (await notificacoes.ObterParaCobranca(parcelaId, ct) is not { } parcela)
            return Result.Falha(ParcelaNaoCobravel);

        var regras = await ReguaDaTurma.Garantir(notificacoes, unitOfWork, ct);
        var hoje = JanelaDeEnvio.Hoje(DateTime.UtcNow);

        if (Avulsa(regras, parcela, hoje) is not { } regra)
            return Result.Falha(Erro.Conflito("notificacao.sem_degrau", "A régua não tem nenhum degrau ativo para usar nesta cobrança."));

        var ja = await notificacoes.ListarChavesDoDia(hoje, ct);
        if (ja.Contains(new ChaveDeEnvio(regra.Id, parcelaId)))
            return Result.Falha(Erro.Conflito("notificacao.ja_cobrada_hoje", "Esta parcela já foi cobrada hoje."));

        var atraso = (await parcelas.ObterRegrasDeAtraso([parcela.VinculoId], ct)).GetValueOrDefault(parcela.VinculoId, RegrasDeAtraso.Nenhuma);
        var valor = ValorDoDia.Calcular(parcela.ValorOriginalEmCentavos, parcela.Vencimento, hoje, atraso);

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
    /// Sem degrau de atraso configurado, cai no de vencimento com o maior deslocamento — o texto mais
    /// severo que a turma escreveu. Sem nenhum de vencimento ativo, não há o que mandar.
    /// </remarks>
    private static RegraResumo? Avulsa(IReadOnlyList<RegraResumo> regras, ParcelaParaCobranca parcela, DateOnly hoje)
    {
        var atraso = hoje.DayNumber - parcela.Vencimento.DayNumber;

        var candidatas = regras.Where(r => r.Ativa && r.Gatilho is GatilhoDaRegua.Vencimento).ToList();

        return candidatas.Where(r => r.DiasDeDeslocamento <= atraso).MaxBy(r => r.DiasDeDeslocamento) ?? candidatas.MinBy(r => r.DiasDeDeslocamento);
    }

    /// <summary>Um item por tipo opcional, mais a cobrança, que aparece marcada e travada.</summary>
    private static IReadOnlyList<PreferenciaResumo> Montar(IReadOnlyList<PreferenciaDeNotificacao> gravadas) =>
        [
            .. Enum.GetValues<TipoDeNotificacao>()
                .Select(tipo => new PreferenciaResumo(
                    tipo,
                    !TiposDeNotificacao.Opcional(tipo) || gravadas.FirstOrDefault(p => p.Tipo == tipo)?.Ativa != false,
                    !TiposDeNotificacao.Opcional(tipo)
                )),
        ];

    private async Task<string> NomeDaFormatura(Guid formaturaId, CancellationToken ct) =>
        (await formaturas.ObterDetalhe(formaturaId, ct))?.Nome ?? _aplicacao.Nome;
}
