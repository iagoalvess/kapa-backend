using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Interfaces;
using Backend.Business.Arquivos.Models;
using Backend.Business.Arquivos.Services;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Services;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Pagamentos.Services;

/// <summary>
/// O lado da tesouraria no caminho do dinheiro: conferência em lote, recusa, baixa manual, estorno,
/// cancelamento avulso e divergências.
/// </summary>
/// <remarks>
/// Toda baixa passa por <see cref="BaixaService"/>, dentro de uma transação que trava a parcela antes de
/// ler o informe. O lado do formando — extrato, cobrança e o "já paguei" — é o <see cref="PagamentoService"/>.
/// <para>Log só com ids: parcela e informe dizem quanto alguém deve.</para>
/// </remarks>
/// <param name="parcelaRepository">Parcelas e regras aceitas.</param>
/// <param name="informeRepository">Avisos de pagamento.</param>
/// <param name="recebimentoRepository">Entradas no caixa.</param>
/// <param name="vinculoRepository">E-mails dos formandos.</param>
/// <param name="formaturaRepository">Nome da turma, para os e-mails.</param>
/// <param name="arquivoService">Comprovantes.</param>
/// <param name="baixaService">A porta única da baixa.</param>
/// <param name="emails">Aviso de recusa e de estorno.</param>
/// <param name="baixaValidator">Forma da baixa manual.</param>
/// <param name="loteValidator">Forma do lote.</param>
/// <param name="recusaValidator">Forma da recusa.</param>
/// <param name="estornoValidator">Forma do estorno.</param>
/// <param name="cancelamentoValidator">Forma do cancelamento avulso.</param>
/// <param name="provedor">A cobrança do Mercado Pago da baixa estornada à mão.</param>
/// <param name="estornoDaCobranca">O acréscimo e a cobrança, desfeitos com a última baixa dela (Sprint 42, decisão 4).</param>
/// <param name="valoresADevolver">O parcial da parcela cancelada (Sprint 42, decisão 3).</param>
/// <param name="eventos">Auditoria do cancelamento.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class TesourariaService(
    IParcelaRepository parcelaRepository,
    IInformeRepository informeRepository,
    IRecebimentoRepository recebimentoRepository,
    IVinculoRepository vinculoRepository,
    IFormaturaRepository formaturaRepository,
    IArquivoService arquivoService,
    BaixaService baixaService,
    EmailsDePagamento emails,
    IValidator<BaixaManual> baixaValidator,
    IValidator<ConfirmarInformes> loteValidator,
    IValidator<RecusarInforme> recusaValidator,
    IValidator<EstornarBaixa> estornoValidator,
    IValidator<CancelarParcela> cancelamentoValidator,
    IProvedorDaTurmaRepository provedor,
    EstornoDaCobranca estornoDaCobranca,
    ValoresADevolver valoresADevolver,
    IEventoRepository eventos,
    IUnitOfWork unitOfWork,
    ILogger<TesourariaService> logger
) : ITesourariaService
{
    private static readonly Erro InformeNaoEncontrado = Erro.NaoEncontrado("pagamento.informe_nao_encontrado", "Aviso de pagamento não encontrado.");

    /// <inheritdoc />
    /// <remarks>O devido é o valor da parcela no dia que o formando informou — é com ele que a divergência se mede.</remarks>
    public async Task<Result<PaginaDe<InformeNaFila>>> ListarInformes(
        PaginacaoRequest paginacao,
        FiltroDeInformes filtro,
        CancellationToken ct = default
    )
    {
        var hoje = DataUtils.Hoje();
        var desde = filtro.ConferidosHoje ? DataUtils.InicioDoDiaEmUtc(hoje) : (DateTime?)null;
        var pagina = await informeRepository.Listar(paginacao.Normalizar(), filtro, hoje, desde, ct);

        if (pagina.Itens.Count == 0)
            return pagina;

        var regras = await parcelaRepository.ObterRegrasDeAtraso([.. pagina.Itens.Select(i => i.Parcela.VinculoId).Distinct()], ct);

        return pagina with
        {
            Itens =
            [
                .. pagina.Itens.Select(informe =>
                {
                    var aceitas = regras.GetValueOrDefault(informe.Parcela.VinculoId, RegrasDeAtraso.Nenhuma);
                    var parcela = informe.Parcela;

                    return informe with
                    {
                        Parcela = parcela.ComValorDoDia(hoje, aceitas),
                        DevidoEmCentavos = ValorDoDia
                            .Calcular(parcela.ValorOriginalEmCentavos, parcela.Vencimento, informe.PagoEm, aceitas, parcela.ValorPagoEmCentavos ?? 0)
                            .TotalEmCentavos,
                    };
                }),
            ],
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pede o arquivo ao módulo de arquivos <b>como o formando que o enviou</b>: lá a regra é "cada um vê os
    /// próprios", e quem pode ver o comprovante é decisão daqui — a política de Tesouraria já passou, e o
    /// informe foi achado nesta turma. O mesmo caminho da foto do formando.
    /// </remarks>
    public async Task<Result<ArquivoParaDownload>> BaixarComprovante(Guid informeId, CancellationToken ct = default)
    {
        var comprovante = await informeRepository.ObterComprovante(informeId, ct);
        if (comprovante is null)
            return Erro.NaoEncontrado("pagamento.sem_comprovante", "Este aviso de pagamento não tem comprovante.");

        return await arquivoService.BaixarComprovante(comprovante.ArquivoId, comprovante.EnviadoPorUsuarioId, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A baixa grava a forma correspondente ao meio que o formando escolheu (decisão 4 da Sprint 18), em
    /// vez de supor PIX: quem pagou em dinheiro entra no caixa como dinheiro, sem a tesouraria adivinhar.
    /// Trocar a forma continua possível pela baixa manual — mas essa é sobre parcela sem aviso.
    /// <para>
    /// Uma transação para o lote (decisão 4 da Sprint 9): as parcelas são travadas antes de os informes serem lidos,
    /// então a segunda confirmação simultânea espera a primeira e encontra o informe já confirmado — ignora,
    /// em vez de baixar duas vezes. Informe de outra turma derruba o lote com 404: a lista veio da tela, e
    /// um id que não é daqui é erro de quem montou o pedido.
    /// </para>
    /// </remarks>
    public async Task<Result<ResultadoDaConferencia>> Confirmar(
        Guid formaturaId,
        Guid usuarioId,
        string? enderecoIp,
        ConfirmarInformes lote,
        CancellationToken ct = default
    )
    {
        var validacao = loteValidator.Validar(lote);
        if (validacao.Falhou)
            return Result.Falha<ResultadoDaConferencia>(validacao.Erros);

        var ids = lote.Itens.Select(item => item.InformeId).ToList();
        var nomeDaTurma = await NomeDaTurma(formaturaId, ct);

        var resultado = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var parcelas = (await parcelaRepository.TravarParaBaixa(await informeRepository.ListarParcelas(ids, token), token)).ToDictionary(p =>
                    p.Id
                );
                var informes = (await informeRepository.ListarParaEdicao(ids, token)).ToDictionary(i => i.Id);

                if (informes.Count != ids.Count)
                    return Result.Falha<ResultadoDaConferencia>(InformeNaoEncontrado);

                var vinculos = informes.Values.Select(i => i.VinculoId).Distinct().ToList();
                var regras = await parcelaRepository.ObterRegrasDeAtraso(vinculos, token);
                var enderecos = await vinculoRepository.ListarEmailsDosVinculos(vinculos, token);
                var agora = DateTime.UtcNow;
                var confirmados = 0;

                foreach (var item in lote.Itens)
                {
                    var informe = informes[item.InformeId];

                    if (informe.Status != StatusDoInforme.Pendente || !parcelas.TryGetValue(informe.ParcelaId, out var parcela))
                        continue;

                    var baixa = await baixaService.Baixar(
                        parcela,
                        new DadosDaBaixa(
                            FormasDePagamento.Da(informe.MeioEscolhido),
                            informe.PagoEm,
                            item.ValorRecebidoEmCentavos,
                            null,
                            usuarioId,
                            enderecoIp,
                            agora
                        ),
                        informe,
                        new ContextoDaBaixa(
                            formaturaId,
                            nomeDaTurma,
                            regras.GetValueOrDefault(informe.VinculoId, RegrasDeAtraso.Nenhuma),
                            enderecos.GetValueOrDefault(informe.VinculoId)
                        ),
                        token
                    );

                    if (baixa.Falhou)
                        return Result.Falha<ResultadoDaConferencia>(baixa.Erros);

                    if (baixa.Valor)
                        confirmados++;
                }

                return Result.Ok(new ResultadoDaConferencia(confirmados, ids.Count - confirmados));
            },
            ct
        );

        if (resultado.Sucesso)
            logger.LogInformation(
                "Lote conferido por {UsuarioId}: {Confirmados} confirmados, {Ignorados} ignorados.",
                usuarioId,
                resultado.Valor.Confirmados,
                resultado.Valor.Ignorados
            );

        return resultado;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Trava a parcela antes de ler o informe, como <see cref="Confirmar"/>: sem isso, confirmar e recusar
    /// o mesmo aviso ao mesmo tempo davam certo os dois — o formando recebia os dois e-mails, e o informe
    /// podia terminar recusado sobre uma parcela paga. Quem chega depois encontra o informe já conferido.
    /// </remarks>
    public async Task<Result> Recusar(Guid formaturaId, Guid usuarioId, Guid informeId, RecusarInforme dados, CancellationToken ct = default)
    {
        var validacao = recusaValidator.Validar(dados);
        if (validacao.Falhou)
            return validacao;

        var parcelaIds = await informeRepository.ListarParcelas([informeId], ct);
        if (parcelaIds.Count == 0)
            return Result.Falha(InformeNaoEncontrado);

        var nomeDaTurma = await NomeDaTurma(formaturaId, ct);

        var recusado = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var travada = (await parcelaRepository.TravarParaBaixa(parcelaIds, token)).SingleOrDefault();
                var informe = (await informeRepository.ListarParaEdicao([informeId], token)).SingleOrDefault();
                if (travada is null || informe is null)
                    return Result.Falha(InformeNaoEncontrado);

                var recusa = informe.Recusar(usuarioId, dados.Motivo, DateTime.UtcNow);
                if (recusa.Falhou)
                    return recusa;

                var enderecos = await vinculoRepository.ListarEmailsDosVinculos([informe.VinculoId], token);

                if (enderecos.TryGetValue(informe.VinculoId, out var email))
                    await emails.Recusado(email, nomeDaTurma, travada.Vencimento, informe.ValorEmCentavos, informe.MotivoDaRecusa!, token);

                return Result.Ok();
            },
            ct
        );

        if (recusado.Sucesso)
            logger.LogInformation("Informe {InformeId} recusado por {UsuarioId}.", informeId, usuarioId);

        return recusado;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Parcela com aviso pendente não baixa à mão: a baixa deixaria o aviso na fila, e a conferência
    /// seguinte o confirmaria sobre uma parcela já paga. A tesouraria confirma ou recusa o aviso.
    /// </remarks>
    public async Task<Result<ParcelaResumo>> BaixarManualmente(
        Guid formaturaId,
        Guid usuarioId,
        string? enderecoIp,
        Guid parcelaId,
        BaixaManual dados,
        NovoArquivo? comprovante,
        CancellationToken ct = default
    )
    {
        var validacao = baixaValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ParcelaResumo>(validacao.Erros);

        var parcela = await parcelaRepository.Obter(parcelaId, DataUtils.Hoje(), ct);
        if (parcela is null)
            return ErrosDePagamento.ParcelaNaoEncontrada;

        if (parcela.Status == StatusDaParcela.Paga)
            return Erro.Conflito("pagamento.parcela_paga", "Esta parcela já está paga.");

        if (!parcela.EmAberto)
            return ErrosDePagamento.ParcelaNaoAberta;

        if (parcela.EmConferencia)
            return Erro.Conflito(
                "pagamento.informe_pendente",
                "O formando já avisou o pagamento desta parcela. Confirme ou recuse o aviso na Conferência."
            );

        var arquivo = await ComprovanteDePagamento.Enviar(arquivoService, comprovante, usuarioId, ct);
        if (arquivo.Falhou)
            return Result.Falha<ParcelaResumo>(arquivo.Erros);

        var nomeDaTurma = await NomeDaTurma(formaturaId, ct);

        var baixa = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var travada = (await parcelaRepository.TravarParaBaixa([parcelaId], token)).Single();

                if (await informeRepository.ExistePendente([parcelaId], token))
                    return Result.Falha(
                        Erro.Conflito(
                            "pagamento.informe_pendente",
                            "O formando já avisou o pagamento desta parcela. Confirme ou recuse o aviso na Conferência."
                        )
                    );

                var regras = await parcelaRepository.ObterRegrasDeAtraso([travada.VinculoId], token);
                var enderecos = await vinculoRepository.ListarEmailsDosVinculos([travada.VinculoId], token);

                var baixou = await baixaService.Baixar(
                    travada,
                    new DadosDaBaixa(dados.Forma, dados.PagoEm, dados.ValorEmCentavos, arquivo.Valor, usuarioId, enderecoIp, DateTime.UtcNow),
                    null,
                    new ContextoDaBaixa(
                        formaturaId,
                        nomeDaTurma,
                        regras.GetValueOrDefault(travada.VinculoId, RegrasDeAtraso.Nenhuma),
                        enderecos.GetValueOrDefault(travada.VinculoId)
                    ),
                    token
                );

                if (baixou.Falhou)
                    return Result.Falha(baixou.Erros);

                return baixou.Valor ? Result.Ok() : Result.Falha(Erro.Conflito("pagamento.parcela_paga", "Esta parcela já está paga."));
            },
            ct
        );

        if (baixa.Falhou)
        {
            await arquivoService.DescartarComprovante(arquivo.Valor, usuarioId, ct);
            return Result.Falha<ParcelaResumo>(baixa.Erros);
        }

        logger.LogWarning("Parcela {ParcelaId} baixada manualmente por {UsuarioId}.", parcelaId, usuarioId);

        return await Reler(parcelaId, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Não apaga nada: o recebimento fica, marcado como estornado, e o evento do estorno se soma ao da baixa
    /// na auditoria (decisão 6). O informe confirmado também fica como está — é o histórico do que o
    /// formando disse; se ele pagar de novo, avisa de novo.
    /// <para>
    /// O formando é avisado por e-mail, com a justificativa: a parcela dele volta a ser devida, e
    /// descobrir isso sozinho no extrato é o que vira ligação para a comissão.
    /// </para>
    /// <para>
    /// Baixa do Mercado Pago também estorna (Sprint 42, decisão 4): é o fluxo manual de quando a comissão devolveu pelo
    /// painel dele. O Kapa não devolve dinheiro — só desfaz o registro. Sem outra baixa ativa da mesma cobrança, ela
    /// passa a <c>Estornada</c> e o acréscimo do cartão sai do caixa, como no estorno que o aviso do Mercado Pago faz;
    /// o aviso que chegar depois não acha o que desfazer. A cobrança é travada <b>antes</b> da parcela, na mesma ordem
    /// do aviso, para os dois não se esperarem em círculo.
    /// </para>
    /// </remarks>
    public async Task<Result<ParcelaResumo>> Estornar(
        Guid formaturaId,
        Guid usuarioId,
        string? enderecoIp,
        Guid parcelaId,
        EstornarBaixa dados,
        CancellationToken ct = default
    )
    {
        var validacao = estornoValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ParcelaResumo>(validacao.Erros);

        var nomeDaTurma = await NomeDaTurma(formaturaId, ct);
        var cobrancaId = await recebimentoRepository.ObterCobrancaDaBaixaAtiva(parcelaId, ct);

        var estorno = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var cobranca = cobrancaId is { } id ? await provedor.TravarCobranca(id, token) : null;

                var parcela = (await parcelaRepository.TravarParaBaixa([parcelaId], token)).SingleOrDefault();
                if (parcela is null)
                    return Result.Falha(ErrosDePagamento.ParcelaNaoEncontrada);

                var recebimento = await recebimentoRepository.ObterAtivoParaEdicao(parcelaId, token);
                if (recebimento is null)
                    return Result.Falha(Erro.Conflito("pagamento.parcela_nao_paga", "Esta parcela não tem baixa para estornar."));

                var enderecos = await vinculoRepository.ListarEmailsDosVinculos([parcela.VinculoId], token);

                var estornado = await baixaService.Estornar(
                    parcela,
                    recebimento,
                    usuarioId,
                    dados.Justificativa,
                    enderecoIp,
                    new ContextoDaBaixa(formaturaId, nomeDaTurma, RegrasDeAtraso.Nenhuma, enderecos.GetValueOrDefault(parcela.VinculoId)),
                    token
                );

                if (
                    estornado.Sucesso
                    && cobranca is { Status: StatusDaCobrancaBancaria.Paga }
                    && recebimento.CobrancaId == cobranca.Id
                    && !await recebimentoRepository.ExisteOutroAtivoDaCobranca(cobranca.Id, recebimento.Id, token)
                )
                    await estornoDaCobranca.Desfazer(cobranca, MotivoDoEstornoAMao, token);

                return estornado;
            },
            ct
        );

        if (estorno.Falhou)
            return Result.Falha<ParcelaResumo>(estorno.Erros);

        logger.LogWarning("Baixa da parcela {ParcelaId} estornada por {UsuarioId}.", parcelaId, usuarioId);

        return await Reler(parcelaId, ct);
    }

    /// <summary>O motivo que vai na descrição do estorno do acréscimo quando o Presidente desfaz a baixa do Mercado Pago.</summary>
    public const string MotivoDoEstornoAMao = "estornado à mão";

    /// <inheritdoc />
    /// <remarks>
    /// Aberta ou vencida cancela; paga não — o dinheiro entrou, e o caminho é estornar a baixa antes. Aviso pendente
    /// também barra, como na baixa manual: cancelar deixaria na fila um aviso sobre uma parcela que não se deve mais.
    /// O que já tinha entrado nela vai para a lista "a devolver" (decisão 3), na mesma transação.
    /// </remarks>
    public async Task<Result<ParcelaResumo>> Cancelar(
        Guid formaturaId,
        Guid usuarioId,
        Guid parcelaId,
        CancelarParcela dados,
        CancellationToken ct = default
    )
    {
        var validacao = cancelamentoValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ParcelaResumo>(validacao.Erros);

        var cancelada = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var parcela = (await parcelaRepository.TravarParaBaixa([parcelaId], token)).SingleOrDefault();
                if (parcela is null)
                    return Result.Falha(ErrosDePagamento.ParcelaNaoEncontrada);

                if (parcela.Status == StatusDaParcela.Paga)
                    return Result.Falha(Erro.Conflito("pagamento.parcela_paga", "Esta parcela já está paga. Estorne a baixa antes de cancelar."));

                if (await informeRepository.ExistePendente([parcelaId], token))
                    return Result.Falha(
                        Erro.Conflito(
                            "pagamento.informe_pendente",
                            "O formando já avisou o pagamento desta parcela. Confirme ou recuse o aviso na Conferência."
                        )
                    );

                if (!parcela.Cancelar(DataUtils.Hoje(), incluirVencidas: true))
                    return Result.Falha(ErrosDePagamento.ParcelaNaoAberta);

                var aDevolver = await valoresADevolver.RegistrarParciais([parcela], token);

                await eventos.Auditar(
                    NomesDeAuditoria.ParcelaCancelada,
                    usuarioId,
                    new
                    {
                        formaturaId,
                        parcelaId,
                        parcela.VinculoId,
                        parcela.ItemDeCobrancaId,
                        parcela.Numero,
                        parcela.Vencimento,
                        valorOriginalEmCentavos = parcela.ValorOriginalEmCentavos,
                        aDevolverEmCentavos = aDevolver,
                        justificativa = dados.Justificativa.Trim(),
                    },
                    token
                );

                return Result.Ok();
            },
            ct
        );

        if (cancelada.Falhou)
            return Result.Falha<ParcelaResumo>(cancelada.Erros);

        logger.LogWarning("Parcela {ParcelaId} cancelada à mão por {UsuarioId}.", parcelaId, usuarioId);

        return await Reler(parcelaId, ct);
    }

    /// <inheritdoc />
    public async Task<Result<PaginaDe<Divergencia>>> ListarDivergencias(
        PaginacaoRequest paginacao,
        string? busca = null,
        CancellationToken ct = default
    ) => Result.Ok(await recebimentoRepository.ListarDivergencias(paginacao.Normalizar(), DataUtils.Hoje(), busca, ct));

    /// <summary>A parcela depois da escrita, com o valor do dia.</summary>
    private async Task<Result<ParcelaResumo>> Reler(Guid parcelaId, CancellationToken ct)
    {
        var hoje = DataUtils.Hoje();
        var parcela = await parcelaRepository.Obter(parcelaId, hoje, ct);

        return parcela is null ? ErrosDePagamento.ParcelaNaoEncontrada : (await parcelaRepository.ComValorDoDia([parcela], hoje, ct))[0];
    }

    private async Task<string> NomeDaTurma(Guid formaturaId, CancellationToken ct) =>
        await formaturaRepository.ObterNome(formaturaId, ct) ?? string.Empty;
}
