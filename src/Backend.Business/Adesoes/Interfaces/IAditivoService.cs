using Backend.Business.Abstractions;
using Backend.Business.Adesoes.Models;
using Backend.Business.Legal.Models;

namespace Backend.Business.Adesoes.Interfaces;

/// <summary>
/// A cesta do formando depois da adesão, e o aditivo que a faz crescer (Sprint 48, D7/D38).
/// </summary>
public interface IAditivoService
{
    /// <summary>O que está na cesta e o que o aditivo pode acrescentar.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">O próprio.</param>
    Task<Result<MinhaCesta>> ObterCesta(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>O aditivo antes do aceite: o que muda, as parcelas novas e o hash, sem gravar nada.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">O próprio.</param>
    /// <param name="pacotes">Os pacotes que entram.</param>
    Task<Result<PreviaDoAditivo>> Simular(Guid formaturaId, Guid usuarioId, IReadOnlyList<Guid> pacotes, CancellationToken ct = default);

    /// <summary>Manda ao e-mail da conta o código que confirma o aceite do aditivo.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">O próprio.</param>
    Task<Result<CodigoEnviado>> SolicitarCodigo(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Aceita o aditivo: a cesta muda, as parcelas da diferença nascem e os convites novos saem.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">O próprio.</param>
    /// <param name="dados">Pacotes, hash da prévia, código e o detalhe livre de cada um.</param>
    /// <param name="origem">IP e navegador do aceite.</param>
    Task<Result<PreviaDoAditivo>> Aceitar(
        Guid formaturaId,
        Guid usuarioId,
        AceitarAditivo dados,
        OrigemDoAceite origem,
        CancellationToken ct = default
    );
}
