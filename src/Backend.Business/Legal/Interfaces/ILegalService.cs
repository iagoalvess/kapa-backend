using Backend.Business.Abstractions;
using Backend.Business.Legal.Models;

namespace Backend.Business.Legal.Interfaces;

/// <summary>
/// Documentos legais da plataforma e consentimento auditável.
/// </summary>
public interface ILegalService
{
    /// <summary>A versão vigente de cada documento.</summary>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<IReadOnlyList<VersaoDeDocumento>>> ListarVigentes(CancellationToken ct = default);

    /// <summary>Uma versão específica, para o link permanente do registro de aceite.</summary>
    /// <param name="tipo">Documento, em qualquer caixa.</param>
    /// <param name="versao">Rótulo da versão.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<VersaoDeDocumento>> ObterVersao(string tipo, string versao, CancellationToken ct = default);

    /// <summary>
    /// Registra o aceite das versões informadas.
    /// </summary>
    /// <remarks>
    /// Só aceita a versão <b>vigente</b>: aceitar uma versão velha provaria concordância com um
    /// texto que já não vale. Chamado dentro de uma transação, o gravado entra nela — é assim
    /// que o cadastro cria conta e consentimento juntos.
    /// </remarks>
    /// <param name="usuarioId">Titular.</param>
    /// <param name="aceites">Versões aceitas.</param>
    /// <param name="origem">IP e navegador de onde veio o aceite.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> RegistrarAceites(Guid usuarioId, IReadOnlyList<AceiteDeDocumento> aceites, OrigemDoAceite origem, CancellationToken ct = default);

    /// <summary>Histórico do usuário e as versões vigentes que ele ainda não aceitou.</summary>
    /// <param name="usuarioId">Titular.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<MeusAceites>> ObterMeusAceites(Guid usuarioId, CancellationToken ct = default);
}
