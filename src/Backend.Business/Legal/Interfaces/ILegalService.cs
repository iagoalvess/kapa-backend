using Backend.Business.Abstractions;
using Backend.Business.Legal.Models;

namespace Backend.Business.Legal.Interfaces;

/// <summary>
/// Documentos legais da plataforma e consentimento auditável.
/// </summary>
public interface ILegalService
{
    /// <summary>A versão vigente de cada documento.</summary>
    Task<Result<IReadOnlyList<VersaoDeDocumento>>> ListarVigentes(CancellationToken ct = default);

    /// <summary>Uma versão específica, para o link permanente do registro de aceite.</summary>
    /// <param name="tipo">Documento, em qualquer caixa.</param>
    /// <param name="versao">Rótulo da versão.</param>
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
    Task<Result> RegistrarAceites(Guid usuarioId, IReadOnlyList<AceiteDeDocumento> aceites, OrigemDoAceite origem, CancellationToken ct = default);

    /// <summary>Histórico do usuário e as versões vigentes que ele ainda não aceitou.</summary>
    /// <param name="usuarioId">Titular.</param>
    Task<Result<MeusAceites>> ObterMeusAceites(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Revoga um consentimento (LGPD, art. 18, IX).
    /// </summary>
    /// <remarks>
    /// Grava uma linha <b>nova</b> com <c>Revogado</c>; o aceite original fica intacto, porque o
    /// banco recusa alterá-lo e porque um registro de consentimento editável não prova nada.
    /// <para>
    /// A consequência é deliberada e a tela avisa antes: revogar o que é obrigatório para usar a
    /// plataforma devolve aquela versão para as pendências, e o aplicativo pede o aceite de novo na
    /// entrada seguinte. Revogar não é sair — para sair existe o pedido de eliminação.
    /// </para>
    /// </remarks>
    /// <param name="usuarioId">Titular.</param>
    /// <param name="consentimentoId">Registro de aceite a revogar.</param>
    /// <param name="origem">IP e navegador de onde veio a revogação.</param>
    Task<Result> Revogar(Guid usuarioId, Guid consentimentoId, OrigemDoAceite origem, CancellationToken ct = default);
}
