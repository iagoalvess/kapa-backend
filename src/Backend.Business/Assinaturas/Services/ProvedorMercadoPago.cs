using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.MercadoPago.Interfaces;
using Backend.Business.MercadoPago.Models;
using Backend.Business.MercadoPago.Settings;
using Microsoft.Extensions.Options;

namespace Backend.Business.Assinaturas.Services;

/// <summary>
/// A licença cobrada na conta do próprio Kapa no Mercado Pago (Sprint 37), com o token dela — não o OAuth das turmas.
/// </summary>
/// <remarks>
/// Tudo na página do Mercado Pago (P1): o cartão recorrente é uma <c>preapproval</c>, que ele debita a cada ciclo e
/// retenta sozinho quando recusa (P3); o PIX avulso do ciclo e a diferença da subida de plano são uma página de
/// pagamento (Checkout Pro). Nenhum dado de cartão passa pela API do Kapa.
/// <para>
/// Referências externas: a recorrência leva o id da assinatura, e os débitos dela voltam com ele; a página avulsa
/// leva o id da <see cref="CobrancaDaAssinatura"/>. Os ids dos eventos saem do id do pagamento no Mercado Pago —
/// o aviso e a conciliação chegam ao mesmo id, e o índice único de <c>eventos_de_cobranca</c> aplica um só.
/// </para>
/// </remarks>
/// <param name="mercadoPago">A API.</param>
/// <param name="options">O token da conta do Kapa.</param>
public sealed class ProvedorMercadoPago(IMercadoPago mercadoPago, IOptions<MercadoPagoSettings> options) : IProvedorDeAssinatura
{
    /// <summary>Por quanto tempo a página avulsa aceita pagamento. A tela gera outra a cada clique em "Pagar".</summary>
    private static readonly TimeSpan ValidadeDaPagina = TimeSpan.FromDays(7);

    private string Token => options.Value.AccessTokenDoKapa;

    /// <inheritdoc />
    public async Task<Result<SessaoDeCheckout>> CriarCheckout(PedidoDeCheckout pedido, CancellationToken ct = default)
    {
        var titulo = $"Kapa — plano {pedido.PlanoNome}";

        if (pedido.CobrancaId is { } cobranca)
        {
            var pagina = await mercadoPago.CriarPagamentoAvulso(
                Token,
                new PedidoDePagamentoAvulso(
                    cobranca,
                    titulo,
                    pedido.PrecoEmCentavos,
                    pedido.Meio,
                    pedido.EmailDoPagador,
                    pedido.UrlDeRetorno,
                    DateTime.UtcNow + ValidadeDaPagina
                ),
                ct
            );

            return pagina.Map(criada => new SessaoDeCheckout(criada.IdExterno, criada.Url));
        }

        if (string.IsNullOrWhiteSpace(pedido.EmailDoPagador))
            throw new ArgumentException("A recorrência do Mercado Pago exige o e-mail de quem paga.", nameof(pedido));

        var recorrencia = await mercadoPago.CriarRecorrencia(
            Token,
            new PedidoDeRecorrencia(
                pedido.AssinaturaId,
                titulo,
                pedido.PrecoEmCentavos,
                pedido.EmailDoPagador,
                pedido.Ciclo == CicloDeCobranca.Anual ? 12 : 1,
                pedido.UrlDeRetorno,
                pedido.ComecaEm
            ),
            ct
        );

        if (recorrencia.Falhou)
            return Result.Falha<SessaoDeCheckout>(recorrencia.Erros);

        return recorrencia.Valor.UrlParaAutorizar is { } url
            ? new SessaoDeCheckout(recorrencia.Valor.IdExterno, url)
            : Erro.Indisponivel("recebimento.provedor_indisponivel", "O Mercado Pago não devolveu a página de autorização. Tente de novo.");
    }

    /// <inheritdoc />
    /// <remarks>Só a recorrência é cancelada: a página avulsa vence sozinha, e pagá-la depois ainda é registrado.</remarks>
    public Task<Result> Cancelar(string idExterno, CancellationToken ct = default) => mercadoPago.CancelarRecorrencia(Token, idExterno, ct);

    /// <inheritdoc />
    public Task<Result> AtualizarValor(string idExterno, long valorEmCentavos, CancellationToken ct = default) =>
        mercadoPago.AtualizarValorDaRecorrencia(Token, idExterno, valorEmCentavos, ct);

    /// <inheritdoc />
    public Task<Result> Estornar(string idDoPagamento, long valorEmCentavos, Guid chave, CancellationToken ct = default) =>
        mercadoPago.Estornar(Token, idDoPagamento, valorEmCentavos, chave, ct);

    /// <inheritdoc />
    /// <remarks>Os avisos do Mercado Pago chegam pelo recebedor único e passam por <see cref="Traduzir"/>.</remarks>
    public Result<EventoDoProvedor> LerWebhook(string corpo, string? assinaturaHmac) =>
        Result.Falha<EventoDoProvedor>(
            Erro.NaoAutenticado("webhook.assinatura_invalida", "Os avisos do Mercado Pago chegam por /webhooks/cobranca/mercadopago.")
        );

    /// <inheritdoc />
    public async Task<Result<EventoDoProvedor?>> ConsultarPagamento(Guid referencia, string? idExterno, CancellationToken ct = default)
    {
        var busca = await mercadoPago.BuscarPagamentoAprovado(Token, referencia, ct);

        if (busca.Falhou)
            return Result.Falha<EventoDoProvedor?>(busca.Erros);

        return busca.Valor is { } pagamento ? Pago(pagamento, referencia, idExterno) : Result.Ok<EventoDoProvedor?>(null);
    }

    /// <summary>
    /// Traduz um aviso da conta do Kapa, perguntando ao Mercado Pago o que aconteceu. Nulo é aviso que não muda nada.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>payment</c>: o pagamento da página avulsa. O que veio de uma recorrência é ignorado aqui — chega
    /// também como débito, com a assinatura.</item>
    /// <item><c>subscription_authorized_payment</c>: o débito de um ciclo do cartão. Recusado vira um evento por
    /// débito, e não por tentativa: o Presidente é avisado na primeira recusa, e o Mercado Pago segue retentando (P3).</item>
    /// <item><c>subscription_preapproval</c>: a recorrência autorizada ou cancelada.</item>
    /// </list>
    /// </remarks>
    /// <param name="tipo">O tópico do aviso.</param>
    /// <param name="idDoRecurso">O <c>data.id</c>.</param>
    public async Task<Result<EventoDoProvedor?>> Traduzir(string? tipo, string? idDoRecurso, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idDoRecurso))
            return Result.Ok<EventoDoProvedor?>(null);

        switch (tipo)
        {
            case "payment":
            {
                var consulta = await mercadoPago.ConsultarPagamento(Token, idDoRecurso, ct);
                if (consulta.Falhou)
                    return Result.Falha<EventoDoProvedor?>(consulta.Erros);

                var pagamento = consulta.Valor;

                return
                    !pagamento.DaRecorrencia
                    && pagamento.Situacao == SituacaoDoPagamento.Aprovado
                    && Guid.TryParse(pagamento.Referencia, out var cobranca)
                    ? Pago(pagamento, cobranca, null)
                    : Result.Ok<EventoDoProvedor?>(null);
            }

            case "subscription_authorized_payment":
            {
                var consulta = await mercadoPago.ConsultarDebitoDaRecorrencia(Token, idDoRecurso, ct);
                if (consulta.Falhou)
                    return Result.Falha<EventoDoProvedor?>(consulta.Erros);

                var debito = consulta.Valor;

                if (
                    debito.Situacao is not (SituacaoDoPagamento.Aprovado or SituacaoDoPagamento.Recusado)
                    || debito.IdDaRecorrencia is not { } recorrenciaId
                )
                    return Result.Ok<EventoDoProvedor?>(null);

                var recorrencia = await mercadoPago.ConsultarRecorrencia(Token, recorrenciaId, ct);
                if (recorrencia.Falhou)
                    return Result.Falha<EventoDoProvedor?>(recorrencia.Erros);

                if (!Guid.TryParse(recorrencia.Valor.Referencia, out var assinaturaId))
                    return Result.Ok<EventoDoProvedor?>(null);

                return debito.Situacao == SituacaoDoPagamento.Aprovado
                    ? new EventoDoProvedor(
                        $"mp_pagamento_{debito.IdDoPagamento ?? debito.Id}",
                        TiposDeEvento.PagamentoConfirmado,
                        assinaturaId,
                        recorrenciaId,
                        debito.OcorridoEm,
                        IdDoPagamento: debito.IdDoPagamento,
                        ValorEmCentavos: debito.ValorEmCentavos
                    )
                    : new EventoDoProvedor($"mp_recusa_{debito.Id}", TiposDeEvento.PagamentoRecusado, assinaturaId, recorrenciaId, debito.OcorridoEm);
            }

            case "subscription_preapproval":
            {
                var consulta = await mercadoPago.ConsultarRecorrencia(Token, idDoRecurso, ct);
                if (consulta.Falhou)
                    return Result.Falha<EventoDoProvedor?>(consulta.Erros);

                var recorrencia = consulta.Valor;
                var evento = recorrencia.Situacao switch
                {
                    SituacaoDaRecorrencia.Autorizada => TiposDeEvento.RecorrenciaAutorizada,
                    SituacaoDaRecorrencia.Cancelada => TiposDeEvento.AssinaturaCancelada,
                    _ => null,
                };

                return evento is not null && Guid.TryParse(recorrencia.Referencia, out var assinaturaId)
                    ? new EventoDoProvedor($"mp_recorrencia_{idDoRecurso}_{evento}", evento, assinaturaId, idDoRecurso)
                    : Result.Ok<EventoDoProvedor?>(null);
            }

            default:
                return Result.Ok<EventoDoProvedor?>(null);
        }
    }

    /// <summary>
    /// O pagamento aprovado como evento. O da recorrência referencia a assinatura; o da página avulsa, a cobrança.
    /// </summary>
    private static EventoDoProvedor Pago(PagamentoNoMercadoPago pagamento, Guid referencia, string? idExterno) =>
        new(
            $"mp_pagamento_{pagamento.Id}",
            TiposDeEvento.PagamentoConfirmado,
            pagamento.DaRecorrencia ? referencia : null,
            pagamento.DaRecorrencia ? idExterno : null,
            pagamento.AprovadoEm,
            pagamento.DaRecorrencia ? null : referencia,
            pagamento.Id,
            pagamento.ValorEmCentavos
        );
}
