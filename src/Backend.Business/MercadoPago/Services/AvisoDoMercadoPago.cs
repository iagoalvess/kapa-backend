using System.Text.Json;
using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.MercadoPago.Interfaces;
using Backend.Business.MercadoPago.Settings;
using Backend.Business.Pagamentos.Services;
using Backend.Business.Recebimentos.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Business.MercadoPago.Services;

/// <summary>
/// O recebedor único dos avisos do Mercado Pago (webhook): confere a assinatura e encaminha — o que é da conta
/// do Kapa para os planos, o resto para a cobrança da turma (Sprint 25, decisões 10 a 12b; Sprint 35).
/// </summary>
/// <remarks>
/// O webhook é da aplicação do Kapa: os avisos das turmas conectadas por OAuth e os da conta do próprio Kapa
/// chegam na mesma URL, com o mesmo segredo. Quem separa é o <c>user_id</c> do corpo — a conta dona do
/// recurso —, comparado com <see cref="MercadoPagoSettings.ContaDoKapa"/>. O <c>user_id</c> não é assinado,
/// e não precisa: ele só escolhe o caminho, e os dois caminhos perguntam ao Mercado Pago, com o token de
/// cada conta, o que de fato aconteceu. Um <c>user_id</c> forjado leva no máximo a uma consulta que não acha nada.
/// <para>
/// Do lado da turma, o id do pedido leva à cobrança, e o que foi pago é perguntado pela
/// <see cref="BaixaAutomatica"/>. Aviso de pedido que não é do Kapa, ou de outro tipo, responde sucesso e não
/// faz nada — senão o Mercado Pago reentregaria para sempre.
/// </para>
/// <para>
/// Mercado Pago fora do ar na consulta devolve indisponível: o webhook responde 503 e ele reentrega. Se
/// desistir, a conciliação do worker acha o pagamento na rodada seguinte — a da turma
/// (<c>CobrancasDoMercadoPagoJob</c>) e a do Kapa (<c>ConciliacaoDeAssinaturasJob</c>).
/// </para>
/// </remarks>
/// <param name="mercadoPago">A API, e a conferência da assinatura.</param>
/// <param name="provedor">De qual cobrança é o pedido.</param>
/// <param name="escopo">A formatura do processamento — o aviso chega sem sessão.</param>
/// <param name="baixa">A consulta e a baixa.</param>
/// <param name="contaDoKapa">O caminho dos avisos da conta do Kapa.</param>
/// <param name="options">Qual é a conta do Kapa.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class AvisoDoMercadoPago(
    IMercadoPago mercadoPago,
    IProvedorDaTurmaRepository provedor,
    FormaturaDoProcessamento escopo,
    BaixaAutomatica baixa,
    IAvisosDaContaDoKapa contaDoKapa,
    IOptions<MercadoPagoSettings> options,
    ILogger<AvisoDoMercadoPago> logger
) : IAvisoDoMercadoPago
{
    /// <inheritdoc />
    public async Task<Result> Receber(
        string? assinatura,
        string? idDaRequisicao,
        string? idDoRecurso,
        string? tipo,
        string? corpo,
        CancellationToken ct = default
    )
    {
        if (!mercadoPago.AvisoAutentico(assinatura, idDaRequisicao, idDoRecurso))
        {
            logger.LogWarning(
                "Aviso do Mercado Pago com assinatura inválida recusado (data.id {IdDoRecurso}, x-request-id {IdDaRequisicao}, x-signature {Assinatura}).",
                idDoRecurso,
                idDaRequisicao,
                assinatura
            );
            return Result.Falha(Erro.NaoAutenticado("pagamento.aviso_nao_autentico", "Assinatura inválida."));
        }

        if (options.Value.ContaDoKapa is { } kapa && ContaDoAviso(corpo) == kapa)
            return await contaDoKapa.Receber(tipo, idDoRecurso, ct);

        if (tipo != "order" || string.IsNullOrWhiteSpace(idDoRecurso))
            return Result.Ok();

        var cobranca = await provedor.ObterPorIdExternoDeTodasAsFormaturas(idDoRecurso.ToUpperInvariant(), ct);
        if (cobranca is null)
            return Result.Ok();

        escopo.Apontar(cobranca.FormaturaId);

        var conciliada = await baixa.Conciliar(cobranca.CobrancaId, ct);

        return conciliada.Falhou ? Result.Falha(conciliada.Erros) : Result.Ok();
    }

    /// <summary>
    /// O <c>user_id</c> do corpo — a conta dona do recurso. O Mercado Pago o escreve como número ou como texto,
    /// conforme o tópico; corpo torto ou sem ele é nulo, e o aviso segue o caminho da turma.
    /// </summary>
    public static long? ContaDoAviso(string? corpo)
    {
        if (string.IsNullOrWhiteSpace(corpo))
            return null;

        try
        {
            using var json = JsonDocument.Parse(corpo);

            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("user_id", out var conta))
                return null;

            return conta.ValueKind switch
            {
                JsonValueKind.Number when conta.TryGetInt64(out var numero) => numero,
                JsonValueKind.String when long.TryParse(conta.GetString(), out var texto) => texto,
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
