using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Models;

namespace Backend.Business.Assinaturas.Interfaces;

/// <summary>
/// O PSP que cobra a licença.
/// </summary>
/// <remarks>
/// Interface com duas implementações de verdade: o <c>ProvedorFake</c> (desenvolvimento, testes,
/// demonstração comercial) e o provedor real, ainda não escolhido. Sem a fake, rodar a suíte exige
/// rede e credencial de terceiro.
/// <para>
/// Quem implementar um PSP traduz <b>nas duas pontas</b>: o que sai (<see cref="PedidoDeCheckout"/>
/// vira a chamada do PSP, com <c>AssinaturaId</c> como referência externa) e o que chega (o JSON
/// do PSP vira <see cref="EventoDoProvedor"/> com os tipos de <see cref="TiposDeEvento"/>). Falha
/// de rede e timeout do PSP voltam como <c>Erro.Indisponivel</c>, nunca como exceção.
/// </para>
/// </remarks>
public interface IProvedorDeAssinatura
{
    /// <summary>Cria a sessão de pagamento hospedada no provedor.</summary>
    /// <param name="pedido">Plano, valor, referência e URL de retorno.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<SessaoDeCheckout>> CriarCheckout(PedidoDeCheckout pedido, CancellationToken ct = default);

    /// <summary>
    /// Cancela a renovação no provedor, ou expira a sessão de checkout ainda não paga. A vigência
    /// paga não é estornada.
    /// </summary>
    /// <param name="idExterno">Id da assinatura ou da sessão de checkout no provedor.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> Cancelar(string idExterno, CancellationToken ct = default);

    /// <summary>
    /// Verifica a assinatura HMAC do corpo e, só então, traduz o evento.
    /// </summary>
    /// <remarks>
    /// A verificação vem <b>antes</b> de qualquer parse: o endpoint é público, e corpo não assinado
    /// não chega nem a ser lido. Assinatura inválida é <c>Erro.NaoAutenticado</c>.
    /// </remarks>
    /// <param name="corpo">Corpo cru, exatamente como chegou — reserializar muda os bytes e invalida o HMAC.</param>
    /// <param name="assinaturaHmac">Valor do cabeçalho de assinatura.</param>
    Result<EventoDoProvedor> LerWebhook(string corpo, string? assinaturaHmac);

    /// <summary>
    /// Pergunta ao provedor se o pagamento de uma assinatura pendente foi confirmado.
    /// </summary>
    /// <remarks>
    /// É a rede de segurança do webhook que se perdeu. Devolve o evento de confirmação — com o id que
    /// o provedor usaria no webhook, quando ele tem — ou nulo se ainda não houve pagamento.
    /// </remarks>
    /// <param name="assinaturaId">Referência enviada no checkout.</param>
    /// <param name="idExterno">Id da sessão ou assinatura no provedor.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<EventoDoProvedor?>> ConsultarPagamento(Guid assinaturaId, string? idExterno, CancellationToken ct = default);
}
