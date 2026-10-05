using Backend.Business.Loja.Models;

namespace Backend.Business.Cobrancas.Models;

/// <summary>O que o formando informa ao pedir o cancelamento (Sprint 48, D8).</summary>
/// <param name="ItemDeCobrancaId">O pacote da cesta, ou o item do pedido avulso.</param>
/// <param name="Motivo">Por que, se quiser dizer.</param>
public sealed record DadosDaSolicitacao(Guid ItemDeCobrancaId, string? Motivo);

/// <summary>Uma solicitação de cancelamento, como o formando e a comissão a veem.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="UsuarioId">Quem pediu.</param>
/// <param name="Nome">Nome civil do cadastro, ou o da conta.</param>
/// <param name="ItemDeCobrancaId">O pacote ou o item do pedido.</param>
/// <param name="Tipo">Tipo do item.</param>
/// <param name="Descricao">Descrição do item, se houver.</param>
/// <param name="Grupo">Grupo de faixas do pacote, se houver.</param>
/// <param name="PedidoId">O pedido avulso; nulo é pacote da cesta.</param>
/// <param name="Motivo">Por que o formando pediu.</param>
/// <param name="PedidoEm">Quando pediu, em UTC.</param>
/// <param name="RespostaAte">Último dia do prazo de resposta — e da suspensão das parcelas (D37).</param>
/// <param name="Status"><c>Aberto</c>, <c>Aprovado</c> ou <c>Recusado</c>.</param>
/// <param name="MotivoDaResposta">O motivo da recusa.</param>
/// <param name="RespondidoEm">Quando a comissão respondeu, em UTC.</param>
/// <param name="PagoEmCentavos">O que já entrou pelas parcelas do item — o que vira "a devolver" se aprovar (D9).</param>
public sealed record ResumoDaSolicitacao(
    Guid Id,
    Guid UsuarioId,
    string Nome,
    Guid ItemDeCobrancaId,
    TipoDeCobranca Tipo,
    string? Descricao,
    string? Grupo,
    Guid? PedidoId,
    string? Motivo,
    DateTime PedidoEm,
    DateOnly RespostaAte,
    StatusDoPedidoDeCancelamento Status,
    string? MotivoDaResposta,
    DateTime? RespondidoEm,
    long PagoEmCentavos
);
