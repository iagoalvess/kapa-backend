using Backend.Business.Abstractions;
using Backend.Business.Assinaturas.Interfaces;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Assinaturas.Services;

/// <summary>
/// Com a assinatura no <c>ProvedorFake</c> (<c>Assinaturas:Provedor=Fake</c>), o aviso
/// da conta do Kapa é registrado e respondido com sucesso — senão o Mercado Pago o reentregaria para sempre.
/// </summary>
/// <param name="logger">Log estruturado.</param>
public sealed class AvisosDaContaDoKapaSemProvedor(ILogger<AvisosDaContaDoKapaSemProvedor> logger) : IAvisosDaContaDoKapa
{
    /// <inheritdoc />
    public Task<Result> Receber(string? tipo, string? idDoRecurso, CancellationToken ct = default)
    {
        logger.LogInformation("Aviso {Tipo} {IdDoRecurso} da conta do Kapa no Mercado Pago; os planos ainda não passam por ele.", tipo, idDoRecurso);

        return Task.FromResult(Result.Ok());
    }
}
