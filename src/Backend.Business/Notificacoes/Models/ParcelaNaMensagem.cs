using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Notificacoes.Models;

/// <summary>
/// Uma parcela e quanto ela vale hoje — o que entra na mensagem.
/// </summary>
/// <param name="Parcela">A parcela alcançada pelo degrau.</param>
/// <param name="Valor">O valor do dia, pelas regras do snapshot da adesão do dono.</param>
public sealed record ParcelaNaMensagem(ParcelaParaCobranca Parcela, ValorDoDia Valor);
