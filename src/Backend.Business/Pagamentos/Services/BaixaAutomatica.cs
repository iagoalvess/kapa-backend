using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Services;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Loja.Services;
using Backend.Business.MercadoPago.Interfaces;
using Backend.Business.MercadoPago.Models;
using Backend.Business.Pagamentos.Interfaces;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Interfaces;
using Backend.Business.Recebimentos.Models;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Pagamentos.Services;

/// <summary>
/// A baixa que chega sozinha: consulta a cobrança no Mercado Pago e, se foi paga, baixa as parcelas
/// (Sprint 25, Parte C). O aviso de pagamento e o job de conciliação chamam o mesmo método.
/// </summary>
/// <remarks>
/// Não é um caminho novo: é mais um chamador do <see cref="BaixaService.Baixar"/>, como a conferência e
/// a baixa manual (decisão 10). O valor vem sempre da consulta à API do Mercado Pago, nunca do corpo do
/// aviso — aceitar o que o aviso afirma deixaria qualquer um quitar a parcela alheia com um <c>curl</c>
/// (decisão 12).
/// <para>
/// Duas barreiras contra a baixa em dobro (decisão 11): a cobrança é travada e só baixa uma vez — aviso
/// e conciliação chegando juntos se enfileiram na trava, e o segundo encontra <c>Paga</c> —; e o
/// <see cref="BaixaService"/> ignora parcela que não está aberta. A consulta ao Mercado Pago roda antes da
/// transação, nunca dentro (decisão 12a).
/// </para>
/// <para>
/// Encerrada é o que o Mercado Pago diz que não vai mais ser pago — ou o que segue "aberto" lá um dia
/// depois de vencer: a conciliação não pode consultar para sempre um pedido que ninguém vai pagar.
/// </para>
/// <para>
/// Cobrança de uma conta que não está mais conectada — desconectada, ou trocada (P3) — não se consulta:
/// o token de agora não enxerga o pedido. Ao vencer, sai da conciliação; o que alguém pagar no meio-tempo
/// chega pela conferência da Sprint 9.
/// </para>
/// <para>
/// Quem "baixou" é quem conectou o Mercado Pago: o recebimento pede um usuário, e é a autorização dele
/// que trouxe o dinheiro. Para o caixa, a linha é igual à da baixa manual. Aviso pendente do formando
/// sobre a mesma parcela é confirmado junto — senão sobraria na fila da tesouraria uma parcela já paga.
/// </para>
/// <para>
/// Sprint 39: no pagamento, a tarifa do Mercado Pago entra no caixa como despesa paga (P6), e a taxa repassada a
/// quem pagou, como receita — o balancete fecha com o extrato dele linha a linha. E a cobrança paga que o Mercado
/// Pago diz ter voltado ao pagador (contestação no cartão ou devolução pelo painel) desfaz o que ela pagou — as
/// baixas, ou a compra da loja — e avisa a comissão (P4).
/// </para>
/// </remarks>
/// <param name="provedor">Credencial e cobranças.</param>
/// <param name="mercadoPago">A API.</param>
/// <param name="parcelaRepository">Parcelas e regras aceitas.</param>
/// <param name="informeRepository">Avisos pendentes.</param>
/// <param name="vinculoRepository">E-mails dos formandos.</param>
/// <param name="formaturaRepository">Nome da turma.</param>
/// <param name="baixaService">A baixa em si.</param>
/// <param name="pagamentoDaCompra">A confirmação da compra da loja, quando a cobrança é dela (Sprint 26).</param>
/// <param name="eventos">Auditoria das pendências — o que foi pago sem parcela para baixar.</param>
/// <param name="recebimentoRepository">As baixas que a devolução desfaz.</param>
/// <param name="despesas">A tarifa do Mercado Pago no caixa (P6).</param>
/// <param name="receitas">A taxa do cartão repassada a quem pagou (P2).</param>
/// <param name="avisos">O aviso da devolução à comissão (P4).</param>
/// <param name="valoresADevolver">O pago sem parcela, que vira pendência da tesouraria (Sprint 42, decisão 9).</param>
/// <param name="estornoDaCobranca">O acréscimo e a cobrança, desfeitos junto com as baixas.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class BaixaAutomatica(
    IProvedorDaTurmaRepository provedor,
    IMercadoPago mercadoPago,
    IParcelaRepository parcelaRepository,
    IInformeRepository informeRepository,
    IVinculoRepository vinculoRepository,
    IFormaturaRepository formaturaRepository,
    BaixaService baixaService,
    PagamentoDaCompra pagamentoDaCompra,
    IEventoRepository eventos,
    IRecebimentoRepository recebimentoRepository,
    IDespesaRepository despesas,
    IOutraReceitaRepository receitas,
    EmailsDePagamento avisos,
    ValoresADevolver valoresADevolver,
    EstornoDaCobranca estornoDaCobranca,
    IUnitOfWork unitOfWork,
    ILogger<BaixaAutomatica> logger
)
{
    /// <summary>
    /// Pagamento que o Mercado Pago confirmou e não achou parcela aberta para baixar, no todo ou em parte — vai para a
    /// lista "a devolver" da tesouraria (Sprint 42, decisão 9), e o evento fica como trilha.
    /// </summary>
    public const string EventoDePagoSemParcela = "pagamento.pago_sem_parcela";

    /// <summary>A cobrança paga voltou ao pagador e o Kapa desfez o que ela pagou (Sprint 39, P4).</summary>
    public const string EventoDeDevolucao = "pagamento.devolvido_no_mercado_pago";

    /// <summary>A justificativa do estorno quando o pagador contesta no cartão — a frase da P4.</summary>
    public const string MotivoDaContestacao = "contestação no cartão";

    /// <summary>A justificativa do estorno quando a turma devolve pelo painel do Mercado Pago.</summary>
    public const string MotivoDaDevolucao = "devolvido no Mercado Pago";

    /// <summary>
    /// Consulta a cobrança no Mercado Pago e baixa as parcelas se ela foi paga — ou, se ela já estava paga e o
    /// dinheiro voltou ao pagador, desfaz o que ela pagou. O escopo precisa estar apontado para a turma da cobrança.
    /// </summary>
    /// <param name="cobrancaId">Cobrança.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Se baixou ou estornou agora; falso quando não havia o que fazer.</returns>
    public async Task<Result<bool>> Conciliar(Guid cobrancaId, CancellationToken ct = default)
    {
        var cobranca = await provedor.ObterCobranca(cobrancaId, ct);
        if (cobranca is not { Status: StatusDaCobrancaBancaria.Emitida or StatusDaCobrancaBancaria.Paga, IdExterno: { } idExterno })
            return false;

        var credencial = await provedor.ObterCredencial(ct);
        if (credencial is null || credencial.IdNoProvedor != cobranca.ContaNoProvedor)
        {
            logger.LogWarning("Cobrança {CobrancaId} de uma conta do Mercado Pago que não está conectada; fica para a conferência.", cobrancaId);

            if (
                cobranca.ExpiraEm < DateTime.UtcNow
                && await provedor.TravarCobranca(cobranca.Id, ct) is { Status: StatusDaCobrancaBancaria.Emitida } vencida
            )
            {
                vencida.Encerrada();
                await unitOfWork.SalvarAsync(ct);
            }

            return false;
        }

        var consulta = await mercadoPago.ConsultarPedido(credencial.AccessToken, idExterno, ct);
        if (consulta.Falhou)
            return Result.Falha<bool>(consulta.Erros);

        var pedido = consulta.Valor;

        if (cobranca.Status == StatusDaCobrancaBancaria.Paga)
            return pedido.Situacao is SituacaoDoPedido.Devolvido or SituacaoDoPedido.Contestado
                ? await Devolver(cobranca.Id, pedido.Situacao, credencial.CadastradaPorUsuarioId, ct)
                : false;

        var vencidaHaUmDia = cobranca.ExpiraEm < DateTime.UtcNow.AddDays(-1);

        if (
            pedido.Situacao is SituacaoDoPedido.Encerrado or SituacaoDoPedido.Devolvido or SituacaoDoPedido.Contestado
            || pedido.Situacao == SituacaoDoPedido.Aberto && vencidaHaUmDia
        )
        {
            if (await provedor.TravarCobranca(cobranca.Id, ct) is { Status: StatusDaCobrancaBancaria.Emitida } travada)
            {
                travada.Encerrada();
                await unitOfWork.SalvarAsync(ct);
            }

            return false;
        }

        if (pedido.Situacao != SituacaoDoPedido.Pago)
            return false;

        if (pedido.Referencia != cobranca.Id.ToString("N"))
        {
            logger.LogError("Pedido {IdExterno} pago com referência que não é a da cobrança {CobrancaId}; não baixa.", idExterno, cobrancaId);
            return false;
        }

        var tarifa = await Tarifa(credencial.AccessToken, cobranca.Id, ct);
        var semAcrescimo = pedido with { ValorPagoEmCentavos = cobranca.SemAcrescimo(pedido.ValorPagoEmCentavos) };

        return cobranca.CompraId is { } compraId
            ? await ConfirmarCompra(cobranca.Id, compraId, cobranca.FormaturaId, semAcrescimo, tarifa, ct)
            : await Baixar(cobranca.Id, cobranca.FormaturaId, credencial.CadastradaPorUsuarioId, semAcrescimo, tarifa, ct);
    }

    /// <summary>
    /// A tarifa que o Mercado Pago descontou do pagamento — o valor menos o líquido que caiu na conta (P6).
    /// </summary>
    /// <remarks>
    /// O pedido não traz a tarifa; o pagamento dele, buscado pela referência, traz o líquido. Consulta antes da
    /// transação, como a do pedido (decisão 12a). <c>ponytail:</c> se a busca falhar, a baixa segue sem a
    /// tarifa e o log avisa — o formando não espera pelo caixa; a tesouraria lança a tarifa à mão se faltar.
    /// </remarks>
    private async Task<long> Tarifa(string accessToken, Guid cobrancaId, CancellationToken ct)
    {
        var pagamento = await mercadoPago.BuscarPagamentoAprovado(accessToken, cobrancaId, ct);

        if (pagamento.Falhou)
            logger.LogWarning("Tarifa da cobrança {CobrancaId} não lida no Mercado Pago; a baixa segue sem ela.", cobrancaId);

        return pagamento is { Sucesso: true, Valor: { } lido } ? lido.TarifaEmCentavos : 0;
    }

    /// <summary>
    /// O que o pagamento deixa no caixa além da baixa: a tarifa do Mercado Pago como despesa paga e, com a taxa do
    /// cartão repassada, o acréscimo que o pagador pagou como receita (P2 e P6). Na transação de quem chama.
    /// </summary>
    private async Task LancarNoCaixa(CobrancaBancaria cobranca, long tarifaEmCentavos, DateOnly pagoEm, CancellationToken ct)
    {
        var referencia = cobranca.Referencia;

        if (tarifaEmCentavos > 0)
            await despesas.Adicionar([Despesa.TarifaDoMercadoPago($"Tarifas do Mercado Pago — {referencia}", tarifaEmCentavos, pagoEm)], ct);

        if (cobranca.AcrescimoEmCentavos <= 0)
            return;

        var acrescimo = OutraReceita.Nova(
            new NovaOutraReceita(
                $"Taxa do cartão paga por quem pagou — {referencia}",
                null,
                CategoriaDeOutraReceita.Outros,
                cobranca.AcrescimoEmCentavos,
                pagoEm,
                Recebida: true
            )
        );
        await receitas.Adicionar(acrescimo, ct);
        cobranca.AcrescimoNoCaixa(acrescimo.Id);
    }

    /// <summary>
    /// A cobrança paga voltou ao pagador (P4): desfaz o que ela pagou, sob a trava da cobrança — aviso e
    /// conciliação juntos desfazem uma vez —, e avisa a comissão.
    /// </summary>
    /// <remarks>
    /// Nas parcelas, estorna a baixa que esta cobrança fez em cada uma (Sprint 42, F3) — a baixa manual que a
    /// tesouraria fez depois não é desta cobrança, e fica; o pago sem parcela dela sai da lista da tesouraria, porque o
    /// dinheiro já voltou. Na loja, cancela a compra pelo caminho da Sprint 38,
    /// sem lista a devolver: o dinheiro já voltou. O acréscimo repassado volta junto — o pagador recebeu tudo —, e a
    /// tarifa lançada fica: o Mercado Pago não a devolve na contestação.
    /// </remarks>
    private async Task<Result<bool>> Devolver(Guid cobrancaId, SituacaoDoPedido situacao, Guid usuarioId, CancellationToken ct)
    {
        var motivo = situacao == SituacaoDoPedido.Contestado ? MotivoDaContestacao : MotivoDaDevolucao;

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var cobranca = await provedor.TravarCobranca(cobrancaId, token);
                if (cobranca is not { Status: StatusDaCobrancaBancaria.Paga })
                    return Result.Ok(false);

                var nomeDaTurma = await formaturaRepository.ObterNome(cobranca.FormaturaId, token) ?? string.Empty;

                var desfeito = cobranca.CompraId is { } compraId
                    ? await pagamentoDaCompra.Devolver(compraId, motivo, usuarioId, token)
                    : await EstornarParcelas(cobranca, motivo, usuarioId, nomeDaTurma, token);

                if (desfeito.Falhou)
                    return Result.Falha<bool>(desfeito.Erros);

                await valoresADevolver.FecharDaCobranca(cobranca.Id, motivo, token);
                await estornoDaCobranca.Desfazer(cobranca, motivo, token);

                foreach (var email in await vinculoRepository.ListarEmailsDaComissao(cobranca.FormaturaId, token))
                    await avisos.DevolvidoNoMercadoPago(email, nomeDaTurma, motivo, cobranca.ValorEmCentavos, desfeito.Valor, token);

                await eventos.Auditar(
                    EventoDeDevolucao,
                    usuarioId,
                    new
                    {
                        formaturaId = cobranca.FormaturaId,
                        cobrancaId,
                        compraId = cobranca.CompraId,
                        parcelaIds = cobranca.ParcelaIds,
                        valorEmCentavos = cobranca.ValorEmCentavos,
                        motivo,
                    },
                    token
                );

                await unitOfWork.SalvarAsync(token);

                logger.LogWarning("Cobrança {CobrancaId} voltou ao pagador ({Motivo}): {Desfeito} desfeito.", cobrancaId, motivo, desfeito.Valor);

                return Result.Ok(true);
            },
            ct
        );
    }

    /// <summary>Estorna as baixas desta cobrança nas parcelas dela; devolve o que foi desfeito, para o e-mail da comissão.</summary>
    private async Task<Result<string>> EstornarParcelas(
        CobrancaBancaria cobranca,
        string motivo,
        Guid usuarioId,
        string nomeDaTurma,
        CancellationToken ct
    )
    {
        var parcelas = await parcelaRepository.TravarParaBaixa(cobranca.ParcelaIds, ct);
        var emailsDosFormandos = await vinculoRepository.ListarEmailsDosVinculos([.. parcelas.Select(p => p.VinculoId).Distinct()], ct);
        var forma = FormasDePagamento.Da(cobranca.Meio);
        var estornadas = 0;

        foreach (var parcela in parcelas)
        {
            if (await recebimentoRepository.ObterAtivoDaCobrancaParaEdicao(parcela.Id, cobranca.Id, forma, ct) is not { } recebimento)
                continue;

            var estorno = await baixaService.Estornar(
                parcela,
                recebimento,
                usuarioId,
                motivo,
                null,
                new ContextoDaBaixa(
                    cobranca.FormaturaId,
                    nomeDaTurma,
                    RegrasDeAtraso.Nenhuma,
                    emailsDosFormandos.GetValueOrDefault(parcela.VinculoId)
                ),
                ct
            );

            if (estorno.Falhou)
                return Result.Falha<string>(estorno.Erros);

            estornadas++;
        }

        return estornadas == 1 ? "a baixa de 1 parcela" : $"a baixa de {estornadas} parcelas";
    }

    /// <summary>
    /// A cobrança paga é de uma compra da loja (Sprint 26): no lugar de baixar parcelas, confirma a compra —
    /// sob a mesma trava da cobrança, então aviso e conciliação juntos confirmam uma vez.
    /// </summary>
    private async Task<Result<bool>> ConfirmarCompra(
        Guid cobrancaId,
        Guid compraId,
        Guid formaturaId,
        PedidoConsultado pedido,
        long tarifaEmCentavos,
        CancellationToken ct
    ) =>
        await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var cobranca = await provedor.TravarCobranca(cobrancaId, token);
                if (cobranca is not { Status: StatusDaCobrancaBancaria.Emitida })
                    return Result.Ok(false);

                var confirmada = await pagamentoDaCompra.Confirmar(compraId, pedido, formaturaId, token);
                cobranca.Paga();
                await LancarNoCaixa(
                    cobranca,
                    tarifaEmCentavos,
                    DateOnly.FromDateTime(DataUtils.ParaExibicao(pedido.PagoEm ?? DateTime.UtcNow)),
                    token
                );
                await unitOfWork.SalvarAsync(token);

                return Result.Ok(confirmada);
            },
            ct
        );

    /// <summary>
    /// Baixa as parcelas da cobrança paga, dividindo o valor da mais antiga para a mais nova — cada uma até
    /// o devido no dia do pagamento, e o que sobrar na última, como no aviso de várias parcelas.
    /// </summary>
    private async Task<Result<bool>> Baixar(
        Guid cobrancaId,
        Guid formaturaId,
        Guid usuarioId,
        PedidoConsultado pedido,
        long tarifaEmCentavos,
        CancellationToken ct
    )
    {
        var pagoEm = DateOnly.FromDateTime(DataUtils.ParaExibicao(pedido.PagoEm ?? DateTime.UtcNow));
        var nomeDaTurma = await formaturaRepository.ObterNome(formaturaId, ct) ?? string.Empty;

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var cobranca = await provedor.TravarCobranca(cobrancaId, token);
                if (cobranca is not { Status: StatusDaCobrancaBancaria.Emitida })
                    return Result.Ok(false);

                var parcelas = (await parcelaRepository.TravarParaBaixa(cobranca.ParcelaIds, token))
                    .OrderBy(p => p.Vencimento)
                    .ThenBy(p => p.Id)
                    .ToList();
                var vinculos = parcelas.Select(p => p.VinculoId).Distinct().ToList();
                var regras = await parcelaRepository.ObterRegrasDeAtraso(vinculos, token);
                var emails = await vinculoRepository.ListarEmailsDosVinculos(vinculos, token);
                var informes = await informeRepository.ListarPendentesParaEdicao(cobranca.ParcelaIds, token);

                var forma = FormasDePagamento.Da(cobranca.Meio);
                var aDistribuir = pedido.ValorPagoEmCentavos;
                var semParcela = 0L;
                Parcela? primeiraSemBaixa = null;
                var baixadas = 0;

                foreach (var (parcela, ultima) in parcelas.Select((p, i) => (p, i == parcelas.Count - 1)))
                {
                    var regrasDoFormando = regras.GetValueOrDefault(parcela.VinculoId, RegrasDeAtraso.Nenhuma);
                    var cabe = ultima ? aDistribuir : Math.Min(aDistribuir, parcela.ValorEm(pagoEm, regrasDoFormando).DevidoEmCentavos);
                    aDistribuir -= cabe;

                    var baixou = await baixaService.Baixar(
                        parcela,
                        new DadosDaBaixa(forma, pagoEm, cabe, null, usuarioId, null, DateTime.UtcNow, cobranca.Id),
                        informes.FirstOrDefault(informe => informe.ParcelaId == parcela.Id),
                        new ContextoDaBaixa(formaturaId, nomeDaTurma, regrasDoFormando, emails.GetValueOrDefault(parcela.VinculoId)),
                        token
                    );

                    if (baixou.Falhou)
                        return Result.Falha<bool>(baixou.Erros);

                    if (baixou.Valor)
                    {
                        baixadas++;
                        continue;
                    }

                    semParcela += cabe;
                    primeiraSemBaixa ??= parcela;
                }

                cobranca.Paga();
                await LancarNoCaixa(cobranca, tarifaEmCentavos, pagoEm, token);

                if (semParcela > 0 || parcelas.Count == 0)
                {
                    logger.LogError("Cobrança {CobrancaId} paga com parte sem parcela aberta para baixar; fica para a tesouraria.", cobrancaId);

                    if (primeiraSemBaixa is not null)
                        await valoresADevolver.RegistrarPagoSemParcela(primeiraSemBaixa, cobranca.Id, semParcela, token);

                    await eventos.Auditar(
                        EventoDePagoSemParcela,
                        usuarioId,
                        new
                        {
                            formaturaId,
                            cobrancaId,
                            valorEmCentavos = parcelas.Count == 0 ? pedido.ValorPagoEmCentavos : semParcela,
                            pagoEm,
                        },
                        token
                    );
                }

                await unitOfWork.SalvarAsync(token);

                logger.LogInformation("Cobrança {CobrancaId} paga no Mercado Pago: {Baixadas} parcela(s) baixada(s).", cobrancaId, baixadas);

                return Result.Ok(baixadas > 0);
            },
            ct
        );
    }
}
