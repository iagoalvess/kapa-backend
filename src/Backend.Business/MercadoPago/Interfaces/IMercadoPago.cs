using Backend.Business.Abstractions;
using Backend.Business.MercadoPago.Models;

namespace Backend.Business.MercadoPago.Interfaces;

/// <summary>
/// A API do Mercado Pago. Tudo o que é vocabulário dele morre na implementação.
/// </summary>
/// <remarks>
/// Genérica de propósito: quem chama passa o token, e o cliente não sabe de quem ele é — o da turma,
/// conectada por OAuth (Sprint 25), ou o do próprio Kapa, cobrando os planos (Sprint 37). Fala dos três
/// meios de <c>MeioDePagamento</c>: o PIX avulso e o cartão por <see cref="Emitir"/>, e o Pix Automático
/// (com o cartão recorrente) por <see cref="CriarRecorrencia"/>. Não é o <c>IProvedorDeCobranca</c> que a
/// Sprint 25 recusou: é a porta de <b>um</b> provedor, com o nome dele, e existe para os testes trocarem a
/// rede por um falso.
/// </remarks>
public interface IMercadoPago
{
    /// <summary>A página onde o dono da conta autoriza a aplicação do Kapa.</summary>
    /// <param name="state">Valor opaco que volta intacto no retorno — quem chama o assina.</param>
    string UrlDeAutorizacao(string state);

    /// <summary>Troca o código do retorno do OAuth pelos tokens da conta que autorizou.</summary>
    /// <param name="codigo">O <c>code</c> do retorno; vale 10 minutos.</param>
    Task<Result<TokensDoMercadoPago>> Autorizar(string codigo, CancellationToken ct = default);

    /// <summary>Renova os tokens antes de o de acesso vencer.</summary>
    /// <param name="refreshToken">Token de renovação atual.</param>
    Task<Result<TokensDoMercadoPago>> Renovar(string refreshToken, CancellationToken ct = default);

    /// <summary>A conta dona do token.</summary>
    /// <param name="accessToken">Token.</param>
    Task<Result<ContaNoMercadoPago>> ConsultarConta(string accessToken, CancellationToken ct = default);

    /// <summary>Cobra uma vez, na conta dona do token: o PIX avulso ou o cartão tokenizado.</summary>
    /// <param name="accessToken">Token.</param>
    /// <param name="pedido">O que cobrar e de quem; a referência é a chave de idempotência.</param>
    /// <returns>O pedido criado, ou falha — no PIX da parcela, quem chama fica com os meios da comissão.</returns>
    Task<Result<DocumentoEmitido>> Emitir(string accessToken, PedidoDeCobranca pedido, CancellationToken ct = default);

    /// <summary>O pedido como está agora — a única fonte do que foi pago (decisão 12 da Sprint 25).</summary>
    /// <param name="accessToken">Token da conta dona do pedido.</param>
    /// <param name="idExterno">Id do pedido.</param>
    Task<Result<PedidoConsultado>> ConsultarPedido(string accessToken, string idExterno, CancellationToken ct = default);

    /// <summary>Cria uma cobrança recorrente de valor fixo, que a pessoa autoriza na página do Mercado Pago.</summary>
    /// <param name="accessToken">Token da conta que recebe.</param>
    /// <param name="pedido">Valor, ciclo, pagador e referência.</param>
    Task<Result<RecorrenciaNoMercadoPago>> CriarRecorrencia(string accessToken, PedidoDeRecorrencia pedido, CancellationToken ct = default);

    /// <summary>A recorrência como está agora — a conciliação do aviso que se perdeu.</summary>
    /// <param name="accessToken">Token da conta que recebe.</param>
    /// <param name="idExterno">Id da recorrência.</param>
    Task<Result<RecorrenciaNoMercadoPago>> ConsultarRecorrencia(string accessToken, string idExterno, CancellationToken ct = default);

    /// <summary>Cancela a recorrência: nenhum débito sai depois disto.</summary>
    /// <param name="accessToken">Token da conta que recebe.</param>
    /// <param name="idExterno">Id da recorrência.</param>
    Task<Result> CancelarRecorrencia(string accessToken, string idExterno, CancellationToken ct = default);

    /// <summary>
    /// Se o aviso veio mesmo do Mercado Pago: confere o <c>x-signature</c> com o segredo do webhook, antes
    /// de o corpo ser lido (decisão 12 da Sprint 25).
    /// </summary>
    /// <param name="assinatura">Cabeçalho <c>x-signature</c> (<c>ts=…,v1=…</c>).</param>
    /// <param name="idDaRequisicao">Cabeçalho <c>x-request-id</c>.</param>
    /// <param name="idDoRecurso">Parâmetro <c>data.id</c> da URL.</param>
    bool AvisoAutentico(string? assinatura, string? idDaRequisicao, string? idDoRecurso);
}
