using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Services;
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
    IUnitOfWork unitOfWork,
    ILogger<BaixaAutomatica> logger
)
{
    /// <summary>Pagamento que o Mercado Pago confirmou e não achou parcela aberta para baixar — pendência da tesouraria.</summary>
    public const string EventoDePagoSemParcela = "pagamento.pago_sem_parcela";

    /// <summary>
    /// Consulta a cobrança no Mercado Pago e baixa as parcelas se ela foi paga. O escopo precisa estar
    /// apontado para a turma da cobrança.
    /// </summary>
    /// <param name="cobrancaId">Cobrança.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Se baixou agora; falso quando não havia o que baixar.</returns>
    public async Task<Result<bool>> Conciliar(Guid cobrancaId, CancellationToken ct = default)
    {
        var cobranca = await provedor.ObterCobranca(cobrancaId, ct);
        if (cobranca is not { Status: StatusDaCobrancaBancaria.Emitida, IdExterno: { } idExterno })
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

        var vencidaHaUmDia = cobranca.ExpiraEm < DateTime.UtcNow.AddDays(-1);

        if (pedido.Situacao == SituacaoDoPedido.Encerrado || pedido.Situacao == SituacaoDoPedido.Aberto && vencidaHaUmDia)
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

        return cobranca.CompraId is { } compraId
            ? await ConfirmarCompra(cobranca.Id, compraId, cobranca.FormaturaId, pedido, ct)
            : await Baixar(cobranca.Id, cobranca.FormaturaId, credencial.CadastradaPorUsuarioId, pedido, ct);
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
                await unitOfWork.SalvarAsync(token);

                return Result.Ok(confirmada);
            },
            ct
        );

    /// <summary>
    /// Baixa as parcelas da cobrança paga, dividindo o valor da mais antiga para a mais nova — cada uma até
    /// o devido no dia do pagamento, e o que sobrar na última, como no aviso de várias parcelas.
    /// </summary>
    private async Task<Result<bool>> Baixar(Guid cobrancaId, Guid formaturaId, Guid usuarioId, PedidoConsultado pedido, CancellationToken ct)
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
                var baixadas = 0;

                foreach (var (parcela, ultima) in parcelas.Select((p, i) => (p, i == parcelas.Count - 1)))
                {
                    var regrasDoFormando = regras.GetValueOrDefault(parcela.VinculoId, RegrasDeAtraso.Nenhuma);
                    var cabe = ultima ? aDistribuir : Math.Min(aDistribuir, parcela.ValorEm(pagoEm, regrasDoFormando).DevidoEmCentavos);
                    aDistribuir -= cabe;

                    var baixou = await baixaService.Baixar(
                        parcela,
                        new DadosDaBaixa(forma, pagoEm, cabe, null, usuarioId, null, DateTime.UtcNow),
                        informes.FirstOrDefault(informe => informe.ParcelaId == parcela.Id),
                        new ContextoDaBaixa(formaturaId, nomeDaTurma, regrasDoFormando, emails.GetValueOrDefault(parcela.VinculoId)),
                        token
                    );

                    if (baixou.Falhou)
                        return Result.Falha<bool>(baixou.Erros);

                    baixadas += baixou.Valor ? 1 : 0;
                }

                cobranca.Paga();

                if (baixadas == 0)
                {
                    logger.LogError("Cobrança {CobrancaId} paga sem parcela aberta para baixar; fica para a tesouraria.", cobrancaId);
                    await eventos.Auditar(
                        EventoDePagoSemParcela,
                        usuarioId,
                        new
                        {
                            formaturaId,
                            cobrancaId,
                            valorEmCentavos = pedido.ValorPagoEmCentavos,
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
