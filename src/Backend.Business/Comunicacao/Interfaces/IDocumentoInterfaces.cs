using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Comunicacao.Models;

namespace Backend.Business.Comunicacao.Interfaces;

/// <summary>
/// O acervo da turma: ata, contrato, orçamento, regulamento.
/// </summary>
/// <remarks>
/// Mesma regra de visibilidade do mural, aplicada no repositório. O download nunca devolve os bytes
/// nem a URL do provedor direto: devolve uma URL assinada de vida curta, emitida só depois de
/// conferir formatura e visibilidade.
/// </remarks>
public interface IDocumentoService
{
    /// <summary>Uma página do acervo, por categoria e título.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem consulta.</param>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Categoria e busca.</param>
    Task<Result<PaginaDe<DocumentoResumo>>> Listar(
        Guid formaturaId,
        Guid usuarioId,
        PaginacaoRequest paginacao,
        FiltroDeDocumentos filtro,
        CancellationToken ct = default
    );

    /// <summary>O acervo em números — quantos, quanto ocupa, o último envio e quantos por categoria.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem consulta.</param>
    Task<Result<ResumoDoAcervo>> Resumir(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Envia um documento novo.</summary>
    /// <remarks>
    /// O tipo é conferido pelos primeiros bytes, não pelo nome, e o tamanho pelo teto do acervo
    /// (<see cref="Documento.TamanhoMaximoEmMB"/> MB).
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem envia — dono do arquivo.</param>
    /// <param name="dados">Título, categoria e visibilidade.</param>
    /// <param name="arquivo">O arquivo; obrigatório.</param>
    Task<Result<DocumentoResumo>> Enviar(
        Guid formaturaId,
        Guid usuarioId,
        DadosDoDocumento dados,
        NovoArquivo? arquivo,
        CancellationToken ct = default
    );

    /// <summary>Corrige título, categoria e visibilidade e, se vier arquivo, substitui o atual pela versão seguinte.</summary>
    /// <remarks>A substituição registra na auditoria o arquivo que saiu e o que entrou; o antigo é apagado.</remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem corrige.</param>
    /// <param name="id">Documento.</param>
    /// <param name="dados">Dados novos.</param>
    /// <param name="arquivo">Arquivo novo, opcional.</param>
    Task<Result<DocumentoResumo>> Atualizar(
        Guid formaturaId,
        Guid usuarioId,
        Guid id,
        DadosDoDocumento dados,
        NovoArquivo? arquivo,
        CancellationToken ct = default
    );

    /// <summary>Exclui um documento e o arquivo dele, registrando na auditoria quem excluiu.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem exclui.</param>
    /// <param name="id">Documento.</param>
    Task<Result> Excluir(Guid formaturaId, Guid usuarioId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// A URL assinada do arquivo, se quem pede pode ver o documento.
    /// </summary>
    /// <remarks>Documento interno pedido por formando responde 404, como o que não existe.</remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    /// <param name="id">Documento.</param>
    Task<Result<string>> Baixar(Guid formaturaId, Guid usuarioId, Guid id, CancellationToken ct = default);
}

/// <summary>
/// Documentos da formatura selecionada.
/// </summary>
/// <remarks>
/// Como no mural, o filtro de visibilidade mora aqui, a partir do papel de quem consulta. Isolados pelo
/// filtro global: nenhum método recebe a formatura.
/// </remarks>
public interface IDocumentoRepository
{
    /// <summary>Uma página do acervo, por categoria e título — ou pelo envio mais recente, se pedido.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Categoria e busca.</param>
    /// <param name="papel">Papel de quem consulta; fora da gestão, só os da turma.</param>
    Task<PaginaDe<DocumentoResumo>> Listar(PaginacaoRequest paginacao, FiltroDeDocumentos filtro, string? papel, CancellationToken ct = default);

    /// <summary>O acervo em números, numa consulta agrupada.</summary>
    /// <param name="papel">Papel de quem consulta.</param>
    Task<ResumoDoAcervo> Resumir(string? papel, CancellationToken ct = default);

    /// <summary>Um documento, como a lista o mostra; nulo se não existir aqui ou o papel não o puder ver.</summary>
    /// <param name="id">Documento.</param>
    /// <param name="papel">Papel de quem consulta.</param>
    Task<DocumentoResumo?> Obter(Guid id, string? papel, CancellationToken ct = default);

    /// <summary>O arquivo atual do documento; nulo se ele não existir aqui ou o papel não o puder ver.</summary>
    /// <param name="id">Documento.</param>
    /// <param name="papel">Papel de quem consulta.</param>
    Task<ArquivoDoDocumento?> ObterArquivo(Guid id, string? papel, CancellationToken ct = default);

    /// <summary>O documento rastreado para alteração; nulo se não existir aqui.</summary>
    /// <param name="id">Documento.</param>
    Task<Documento?> ObterParaEdicao(Guid id, CancellationToken ct = default);

    /// <summary>Marca um documento novo para inclusão.</summary>
    /// <param name="documento">Documento.</param>
    Task Adicionar(Documento documento, CancellationToken ct = default);

    /// <summary>Marca um documento para remoção.</summary>
    /// <param name="documento">Documento rastreado.</param>
    void Remover(Documento documento);
}
