using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Assinaturas.Services;

/// <summary>
/// Os avisos da conta do Kapa no Mercado Pago aplicados na assinatura (Sprint 37): o provedor pergunta ao Mercado
/// Pago o que aconteceu, e o evento segue pelo mesmo caminho do webhook — registro único, efeito na mesma transação.
/// </summary>
/// <remarks>
/// O recebedor único (<c>AvisoDoMercadoPago</c>) já conferiu o <c>x-signature</c>. Só o Mercado Pago <b>fora do ar</b>
/// devolve falha, para ele reentregar; se desistir, a conciliação acha o pagamento. Recurso que ele mesmo responde
/// que não existe (4xx) responde sucesso: reentregar não o faria existir, e o 409 seria reentregue para sempre.
/// </remarks>
/// <param name="provedor">A tradução do aviso.</param>
/// <param name="webhook">A aplicação do evento.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class AvisosDaContaDoKapa(ProvedorMercadoPago provedor, IWebhookService webhook, ILogger<AvisosDaContaDoKapa> logger)
    : IAvisosDaContaDoKapa
{
    /// <inheritdoc />
    public async Task<Result> Receber(string? tipo, string? idDoRecurso, CancellationToken ct = default)
    {
        var traducao = await provedor.Traduzir(tipo, idDoRecurso, ct);

        if (traducao.Falhou)
        {
            if (traducao.PrimeiroErro.Tipo == ETipoErro.Indisponivel)
                return Result.Falha(traducao.Erros);

            logger.LogWarning(
                "Aviso {Tipo} {IdDoRecurso} da conta do Kapa ignorado: o Mercado Pago respondeu {Codigo}.",
                tipo,
                idDoRecurso,
                traducao.PrimeiroErro.Codigo
            );
            return Result.Ok();
        }

        if (traducao.Valor is not { } evento)
            return Result.Ok();

        var aplicado = await webhook.Aplicar(evento, ct);

        return aplicado.Falhou ? Result.Falha(aplicado.Erros) : Result.Ok();
    }
}
