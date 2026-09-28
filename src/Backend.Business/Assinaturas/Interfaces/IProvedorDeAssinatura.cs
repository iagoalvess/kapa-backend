using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;

namespace Backend.Business.Assinaturas.Interfaces;

/// <summary>
/// O PSP que cobra a licença.
/// </summary>
/// <remarks>
/// Duas implementações, escolhidas por <c>Assinaturas:Provedor</c>: o <c>ProvedorFake</c> (desenvolvimento, testes,
/// demonstração comercial) e o <c>ProvedorMercadoPago</c>, com a conta do próprio Kapa (Sprint 37). Sem a fake,
/// rodar a suíte exige rede e credencial de terceiro.
/// <para>
/// Quem implementar um PSP traduz <b>nas duas pontas</b>: o que sai (<see cref="PedidoDeCheckout"/>
/// vira a chamada do PSP, com <c>AssinaturaId</c> — ou a cobrança avulsa — como referência externa) e o que chega
/// (o aviso do PSP vira <see cref="EventoDoProvedor"/> com os tipos de <see cref="TiposDeEvento"/>). Falha
/// de rede e timeout do PSP voltam como <c>Erro.Indisponivel</c>, nunca como exceção.
/// </para>
/// </remarks>
public interface IProvedorDeAssinatura
{
    /// <summary>
    /// Cria a página de pagamento hospedada no provedor: a recorrência no cartão ou, com
    /// <see cref="PedidoDeCheckout.CobrancaId"/>, uma cobrança avulsa (o PIX do ciclo, a diferença de plano).
    /// </summary>
    /// <param name="pedido">Plano, valor, meio, referência e URL de retorno.</param>
    Task<Result<SessaoDeCheckout>> CriarCheckout(PedidoDeCheckout pedido, CancellationToken ct = default);

    /// <summary>
    /// Cancela a renovação no provedor, ou expira a sessão de checkout ainda não paga. A vigência
    /// paga não é estornada.
    /// </summary>
    /// <param name="idExterno">Id da assinatura ou da sessão de checkout no provedor.</param>
    Task<Result> Cancelar(string idExterno, CancellationToken ct = default);

    /// <summary>Muda o valor dos próximos débitos da recorrência — a troca de plano (P4).</summary>
    /// <param name="idExterno">Id da recorrência no provedor.</param>
    /// <param name="valorEmCentavos">Valor novo de cada ciclo.</param>
    Task<Result> AtualizarValor(string idExterno, long valorEmCentavos, CancellationToken ct = default);

    /// <summary>Devolve um pagamento, inteiro ou em parte (P7).</summary>
    /// <param name="idDoPagamento">Id do pagamento no provedor.</param>
    /// <param name="valorEmCentavos">Quanto devolver.</param>
    /// <param name="chave">Chave de idempotência: a mesma, em nova tentativa, não devolve duas vezes.</param>
    Task<Result> Estornar(string idDoPagamento, long valorEmCentavos, Guid chave, CancellationToken ct = default);

    /// <summary>
    /// Verifica a assinatura HMAC do corpo e, só então, traduz o evento.
    /// </summary>
    /// <remarks>
    /// A verificação vem <b>antes</b> de qualquer parse: o endpoint é público, e corpo não assinado
    /// não chega nem a ser lido. Assinatura inválida é <c>Erro.NaoAutenticado</c>. O Mercado Pago não passa
    /// por aqui: os avisos dele chegam pelo recebedor único, <c>AvisoDoMercadoPago</c>.
    /// </remarks>
    /// <param name="corpo">Corpo cru, exatamente como chegou — reserializar muda os bytes e invalida o HMAC.</param>
    /// <param name="assinaturaHmac">Valor do cabeçalho de assinatura.</param>
    Result<EventoDoProvedor> LerWebhook(string corpo, string? assinaturaHmac);

    /// <summary>
    /// Pergunta ao provedor se houve pagamento com esta referência.
    /// </summary>
    /// <remarks>
    /// É a rede de segurança do webhook que se perdeu. Devolve o evento de confirmação — com o id que
    /// o provedor usaria no webhook, quando ele tem — ou nulo se ainda não houve pagamento.
    /// </remarks>
    /// <param name="referencia">A referência enviada: a assinatura, na recorrência; a cobrança, no avulso.</param>
    /// <param name="idExterno">Id da sessão ou assinatura no provedor.</param>
    Task<Result<EventoDoProvedor?>> ConsultarPagamento(Guid referencia, string? idExterno, CancellationToken ct = default);
}
