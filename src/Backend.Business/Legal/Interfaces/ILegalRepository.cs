using Backend.Business.Legal.Models;

namespace Backend.Business.Legal.Interfaces;

/// <summary>
/// Leitura dos documentos legais e gravação do consentimento.
/// </summary>
/// <remarks>
/// Não há método para alterar nem remover: documento e consentimento são append-only, e a
/// ausência do método é a primeira barreira — o gatilho no banco é a segunda.
/// </remarks>
public interface ILegalRepository
{
    /// <summary>A versão vigente de cada documento no instante informado.</summary>
    /// <param name="agora">Instante de referência, em UTC.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<IReadOnlyList<VersaoDeDocumento>> ListarVigentes(DateTime agora, CancellationToken ct = default);

    /// <summary>Uma versão específica, vigente ou não.</summary>
    /// <param name="tipo">Documento, já na grafia oficial.</param>
    /// <param name="versao">Rótulo da versão.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<VersaoDeDocumento?> ObterVersao(string tipo, string versao, CancellationToken ct = default);

    /// <summary>Histórico de consentimento do usuário, do mais recente para o mais antigo.</summary>
    /// <param name="usuarioId">Titular.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<IReadOnlyList<ConsentimentoDoUsuario>> ListarConsentimentos(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Marca um registro de consentimento para gravação.</summary>
    /// <param name="consentimento">Registro a gravar.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task Adicionar(ConsentimentoRegistrado consentimento, CancellationToken ct = default);
}
