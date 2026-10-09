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
    Task<IReadOnlyList<VersaoDeDocumento>> ListarVigentes(DateTime agora, CancellationToken ct = default);

    /// <summary>Só o rótulo da versão vigente de um documento, sem o texto — o que a compra da loja grava como lido.</summary>
    /// <param name="tipo">Documento, já na grafia oficial.</param>
    /// <param name="agora">Instante de referência, em UTC.</param>
    /// <returns>A versão; nula se o documento nunca foi publicado.</returns>
    Task<string?> VersaoVigente(string tipo, DateTime agora, CancellationToken ct = default);

    /// <summary>Uma versão específica, vigente ou não.</summary>
    /// <param name="tipo">Documento, já na grafia oficial.</param>
    /// <param name="versao">Rótulo da versão.</param>
    Task<VersaoDeDocumento?> ObterVersao(string tipo, string versao, CancellationToken ct = default);

    /// <summary>Histórico de consentimento do usuário, do mais recente para o mais antigo.</summary>
    /// <param name="usuarioId">Titular.</param>
    Task<IReadOnlyList<ConsentimentoDoUsuario>> ListarConsentimentos(Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Um registro de consentimento do titular informado.
    /// </summary>
    /// <remarks>
    /// Sem rastreamento, porque não há o que alterar: revogar é <b>inserir</b> uma linha nova. Esta
    /// leitura existe só para a revogação saber qual versão de qual documento ela está desfazendo.
    /// </remarks>
    /// <param name="id">Registro.</param>
    /// <param name="usuarioId">Titular.</param>
    Task<ConsentimentoRegistrado?> ObterConsentimentoDoTitular(Guid id, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Se o titular já revogou este documento depois do aceite informado.</summary>
    /// <remarks>
    /// É o que impede a segunda revogação do mesmo aceite: o histórico é append-only, então "já
    /// revogado" não é um campo do registro original — é a existência de uma linha mais nova.
    /// </remarks>
    /// <param name="usuarioId">Titular.</param>
    /// <param name="documentoLegalId">Versão do documento.</param>
    /// <param name="depoisDe">Momento do aceite que se quer revogar, em UTC.</param>
    Task<bool> TemRevogacaoPosterior(Guid usuarioId, Guid documentoLegalId, DateTime depoisDe, CancellationToken ct = default);

    /// <summary>Marca um registro de consentimento para gravação.</summary>
    /// <param name="consentimento">Registro a gravar.</param>
    Task Adicionar(ConsentimentoRegistrado consentimento, CancellationToken ct = default);
}
