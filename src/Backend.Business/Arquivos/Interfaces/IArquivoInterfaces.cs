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
    Task GravarAsync(string chave, Stream conteudo, string contentType, CancellationToken ct = default);

    /// <summary>Abre o conteúdo para leitura.</summary>
    /// <param name="chave">Caminho do objeto no provedor.</param>
    /// <returns>Fluxo de leitura; quem chama é dono do descarte.</returns>
    /// <exception cref="FileNotFoundException">Se o objeto não existir no provedor.</exception>
    Task<Stream> AbrirLeituraAsync(string chave, CancellationToken ct = default);

    /// <summary>Remove o objeto. Não falha se ele já não existir.</summary>
    /// <param name="chave">Caminho do objeto no provedor.</param>
    Task RemoverAsync(string chave, CancellationToken ct = default);

    /// <summary>Remove todos os objetos cuja chave começa pelo prefixo. Não falha se não houver nenhum.</summary>
    /// <param name="prefixo">Começo da chave, sem a barra final — uma "pasta".</param>
    Task RemoverPrefixoAsync(string prefixo, CancellationToken ct = default);

    /// <summary>
    /// Uma URL que baixa o objeto sem credencial até expirar.
    /// </summary>
    /// <remarks>
    /// Quem pode receber a URL é decisão de quem chama — ela vale para quem a tiver em mãos até o
    /// prazo. Por isso o prazo é de minutos, e ela é emitida só depois de a autorização passar.
    /// </remarks>
    /// <param name="chave">Caminho do objeto no provedor.</param>
    /// <param name="nome">Nome sugerido para o arquivo baixado.</param>
    /// <param name="contentType">Tipo do conteúdo.</param>
    /// <param name="validade">Por quanto tempo a URL vale.</param>
    /// <returns>A URL — absoluta no S3, relativa à API no provedor local.</returns>
    Task<string> GerarUrlTemporariaAsync(string chave, string nome, string contentType, TimeSpan validade);
}

/// <summary>
/// Envio, download e remoção de arquivos.
/// </summary>
/// <remarks>
/// O download padrão (<see cref="Baixar"/>) passa pela API: é mais tráfego, e mantém a autorização
/// em um lugar só. <see cref="GerarUrlTemporaria"/> é a alternativa para o acervo da Sprint 11 —
/// a API autoriza e redireciona para uma URL assinada de minutos, e os bytes não passam por ela. A
/// troca aceita é a de sempre: URL emitida vale para quem a tiver até expirar, mesmo que o usuário
/// perca o acesso no meio do caminho.
/// </remarks>
public interface IArquivoService
{
    /// <summary>Envia um arquivo e devolve o id do registro criado.</summary>
    /// <param name="dados">Nome, tipo, tamanho, conteúdo e categoria.</param>
    /// <param name="enviadoPorId">Usuário autenticado que está enviando.</param>
    Task<Result<Guid>> Enviar(NovoArquivo dados, Guid enviadoPorId, CancellationToken ct = default);

    /// <summary>Abre um arquivo para download.</summary>
    /// <param name="id">Identificador do arquivo.</param>
    /// <param name="solicitante">Quem está pedindo.</param>
    Task<Result<ArquivoParaDownload>> Baixar(Guid id, SolicitanteDeArquivo solicitante, CancellationToken ct = default);

    /// <summary>Uma URL assinada que baixa o arquivo sem credencial, pelo prazo informado.</summary>
    /// <param name="id">Identificador do arquivo.</param>
    /// <param name="solicitante">Quem está pedindo.</param>
    /// <param name="validade">Por quanto tempo a URL vale — minutos, não horas.</param>
    Task<Result<string>> GerarUrlTemporaria(Guid id, SolicitanteDeArquivo solicitante, TimeSpan validade, CancellationToken ct = default);

    /// <summary>Abre o objeto de uma URL temporária do provedor local, se ela saiu daqui e ainda vale.</summary>
    /// <param name="objeto">O que veio na query string.</param>
    Task<Result<ArquivoParaDownload>> AbrirPorUrlTemporaria(ObjetoTemporario objeto, CancellationToken ct = default);

    /// <summary>Remove um arquivo e o objeto correspondente no provedor.</summary>
    /// <param name="id">Identificador do arquivo.</param>
    /// <param name="solicitante">Quem está pedindo.</param>
    Task<Result> Remover(Guid id, SolicitanteDeArquivo solicitante, CancellationToken ct = default);
}

/// <summary>
/// Quem está pedindo a operação.
/// </summary>
/// <remarks>
/// A regra padrão é simples de propósito: **cada um enxerga os próprios arquivos.** Regra além disso —
/// anexo visível para toda a equipe do pedido, documento restrito por filial — depende do domínio e entra
/// no service do projeto.
/// <para>
/// Nenhum perfil vê o arquivo de outra pessoa pela API: até a Sprint 44 o administrador via, e deixou de
/// ver com a D4 — ele é perfil de plataforma, e foto e comprovante são dado pessoal que o painel não usa.
/// Quem age sobre arquivo alheio é só o próprio sistema, fora de uma requisição de usuário: a anonimização
/// e o processamento dos pedidos da LGPD.
/// </para>
/// </remarks>
/// <param name="Id">Identificador de quem pede, ou do titular em nome de quem o sistema age.</param>
/// <param name="PeloSistema">Operação interna do sistema, que alcança arquivo de qualquer titular. Nunca vem de uma requisição HTTP.</param>
public readonly record struct SolicitanteDeArquivo(Guid Id, bool PeloSistema);

/// <summary>
/// Acesso aos metadados dos arquivos.
/// </summary>
public interface IArquivoRepository
{
    /// <summary>Marca um arquivo para inclusão.</summary>
    /// <param name="arquivo">Metadados do arquivo.</param>
    Task Adicionar(Arquivo arquivo, CancellationToken ct = default);

    /// <summary>Obtém um arquivo pelo identificador, rastreado para alteração ou remoção.</summary>
    /// <param name="id">Identificador do arquivo.</param>
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
    Task<UsoDeArmazenamento> ObterUsoDoUsuario(Guid enviadoPorId, CancellationToken ct = default);

    /// <summary>Quanto espaço todos os arquivos do sistema ocupam, em bytes.</summary>
    Task<long> ObterBytesDeTodosOsArquivos(CancellationToken ct = default);
}
