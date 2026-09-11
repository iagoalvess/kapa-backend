using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;

namespace Backend.Business.Arquivos.Interfaces;

/// <summary>
/// Guarda e recupera os bytes de um arquivo.
/// </summary>
/// <remarks>
/// É o único ponto que conhece o provedor. Trabalha só com a chave — não sabe quem enviou, a que
/// categoria pertence nem quem pode baixar; isso é do <see cref="IArquivoService"/>.
/// <para>
/// Trocar de provedor é trocar a implementação registrada. Nada mais no projeto muda.
/// </para>
/// </remarks>
public interface IArmazenamentoDeArquivos
{
    /// <summary>Grava o conteúdo sob a chave informada, sobrescrevendo se já existir.</summary>
    /// <param name="chave">Caminho do objeto no provedor.</param>
    /// <param name="conteudo">Fluxo com os bytes.</param>
    /// <param name="contentType">Tipo do conteúdo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task GravarAsync(string chave, Stream conteudo, string contentType, CancellationToken ct = default);

    /// <summary>Abre o conteúdo para leitura.</summary>
    /// <param name="chave">Caminho do objeto no provedor.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Fluxo de leitura; quem chama é dono do descarte.</returns>
    /// <exception cref="FileNotFoundException">Se o objeto não existir no provedor.</exception>
    Task<Stream> AbrirLeituraAsync(string chave, CancellationToken ct = default);

    /// <summary>Remove o objeto. Não falha se ele já não existir.</summary>
    /// <param name="chave">Caminho do objeto no provedor.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task RemoverAsync(string chave, CancellationToken ct = default);
}

/// <summary>
/// Envio, download, listagem e remoção de arquivos.
/// </summary>
/// <remarks>
/// O download passa pela API em vez de devolver uma URL assinada do provedor. É mais tráfego,
/// e mantém a autorização em um lugar só: URL assinada, uma vez emitida, vale para quem a tiver
/// em mãos, independentemente de o usuário ter perdido o acesso no meio do caminho.
/// <para>
/// O teto disso é conhecido: arquivo muito grande ocupa a conexão da API pelo tempo da
/// transferência. Se isso virar problema, o ponto de mudança é acrescentar a emissão de URL
/// temporária ao <see cref="IArmazenamentoDeArquivos"/> — e aceitar a troca de autorização.
/// </para>
/// </remarks>
public interface IArquivoService
{
    /// <summary>Envia um arquivo.</summary>
    /// <param name="dados">Nome, tipo, tamanho, conteúdo e categoria.</param>
    /// <param name="enviadoPorId">Usuário autenticado que está enviando.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ArquivoResumo>> Enviar(NovoArquivo dados, Guid enviadoPorId, CancellationToken ct = default);

    /// <summary>Abre um arquivo para download.</summary>
    /// <param name="id">Identificador do arquivo.</param>
    /// <param name="solicitante">Quem está pedindo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ArquivoParaDownload>> Baixar(Guid id, SolicitanteDeArquivo solicitante, CancellationToken ct = default);

    /// <summary>Obtém os metadados de um arquivo.</summary>
    /// <param name="id">Identificador do arquivo.</param>
    /// <param name="solicitante">Quem está pedindo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<ArquivoResumo>> ObterPorId(Guid id, SolicitanteDeArquivo solicitante, CancellationToken ct = default);

    /// <summary>Lista arquivos paginados.</summary>
    /// <param name="paginacao">Página e tamanho.</param>
    /// <param name="categoria">Filtro por categoria. Nulo lista todas.</param>
    /// <param name="solicitante">Quem está pedindo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result<PaginaDe<ArquivoResumo>>> Listar(
        PaginacaoRequest paginacao,
        string? categoria,
        SolicitanteDeArquivo solicitante,
        CancellationToken ct = default
    );

    /// <summary>Remove um arquivo e o objeto correspondente no provedor.</summary>
    /// <param name="id">Identificador do arquivo.</param>
    /// <param name="solicitante">Quem está pedindo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Result> Remover(Guid id, SolicitanteDeArquivo solicitante, CancellationToken ct = default);
}

/// <summary>
/// Quem está pedindo a operação.
/// </summary>
/// <remarks>
/// A regra padrão é simples de propósito: **cada um enxerga os próprios arquivos, e o
/// administrador enxerga todos.** Regra além disso — anexo visível para toda a equipe do pedido,
/// documento restrito por filial — depende do domínio e entra no service do projeto.
/// </remarks>
/// <param name="Id">Identificador do usuário autenticado.</param>
/// <param name="EhAdministrador">Se o usuário tem o perfil de administrador.</param>
public readonly record struct SolicitanteDeArquivo(Guid Id, bool EhAdministrador);

/// <summary>
/// Acesso aos metadados dos arquivos.
/// </summary>
public interface IArquivoRepository
{
    /// <summary>Marca um arquivo para inclusão.</summary>
    /// <param name="arquivo">Metadados do arquivo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task Adicionar(Arquivo arquivo, CancellationToken ct = default);

    /// <summary>Obtém um arquivo pelo identificador, rastreado para alteração ou remoção.</summary>
    /// <param name="id">Identificador do arquivo.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<Arquivo?> ObterPorId(Guid id, CancellationToken ct = default);

    /// <summary>Marca um arquivo para remoção.</summary>
    /// <param name="arquivo">Arquivo a remover.</param>
    void Remover(Arquivo arquivo);

    /// <summary>Soma quantos arquivos um usuário já tem e quanto espaço eles ocupam.</summary>
    /// <remarks>
    /// Os dois números saem da mesma consulta: são sempre pedidos juntos, e duas idas ao banco
    /// para conferir uma cota é uma a mais do que o necessário.
    /// </remarks>
    /// <param name="enviadoPorId">Dono dos arquivos.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<UsoDeArmazenamento> ObterUsoDoUsuario(Guid enviadoPorId, CancellationToken ct = default);

    /// <summary>Lista arquivos paginados, opcionalmente restritos a um dono e a uma categoria.</summary>
    /// <param name="paginacao">Página e tamanho já normalizados.</param>
    /// <param name="categoria">Filtro por categoria. Nulo lista todas.</param>
    /// <param name="enviadoPorId">Restringe a um dono. Nulo lista de todos.</param>
    /// <param name="ct">Token de cancelamento.</param>
    Task<PaginaDe<ArquivoResumo>> Listar(PaginacaoRequest paginacao, string? categoria, Guid? enviadoPorId, CancellationToken ct = default);
}
