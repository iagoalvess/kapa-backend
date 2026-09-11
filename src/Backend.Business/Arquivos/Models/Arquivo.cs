using Backend.Business.Abstractions;

namespace Backend.Business.Arquivos.Models;

/// <summary>
/// Metadados de um arquivo armazenado.
/// </summary>
/// <remarks>
/// A divisão é deliberada: **os metadados ficam no banco, os bytes no provedor**. A linha é a
/// fonte da verdade sobre quais arquivos existem, quem os enviou e quem pode baixá-los — sem
/// ela, listar e autorizar exigiria varrer o bucket, e não haveria como saber que um objeto
/// órfão pode ser apagado.
/// </remarks>
public class Arquivo : Entity
{
    /// <summary>Nome original informado no envio, usado no download.</summary>
    public string Nome { get; set; } = string.Empty;

    /// <summary>
    /// Caminho do objeto dentro do provedor.
    /// </summary>
    /// <remarks>
    /// Gerado pela aplicação, nunca derivado do nome enviado pelo usuário. Nome de arquivo vindo
    /// de fora traz <c>../</c>, caractere de controle, unicode ambíguo e colisão — e qualquer um
    /// desses vira leitura ou escrita fora do diretório pretendido.
    /// </remarks>
    public string Chave { get; set; } = string.Empty;

    /// <summary>Tipo do conteúdo, para devolver no download.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Tamanho em bytes.</summary>
    public long Tamanho { get; set; }

    /// <summary>Agrupamento lógico — <c>avatares</c>, <c>anexos-pedido</c>, <c>importacoes</c>.</summary>
    public string Categoria { get; set; } = string.Empty;

    /// <summary>Usuário que enviou o arquivo.</summary>
    public Guid EnviadoPorId { get; set; }
}

/// <summary>Arquivo como aparece em listagem.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome original.</param>
/// <param name="ContentType">Tipo do conteúdo.</param>
/// <param name="Tamanho">Tamanho em bytes.</param>
/// <param name="Categoria">Agrupamento lógico.</param>
/// <param name="EnviadoPorId">Quem enviou.</param>
/// <param name="CriadoEm">Momento do envio, em UTC.</param>
public sealed record ArquivoResumo(Guid Id, string Nome, string ContentType, long Tamanho, string Categoria, Guid EnviadoPorId, DateTime CriadoEm);

/// <summary>Quanto um usuário já ocupa, para conferir a cota antes de aceitar mais um envio.</summary>
/// <param name="Quantidade">Arquivos que o usuário tem hoje.</param>
/// <param name="Bytes">Soma do tamanho deles.</param>
public readonly record struct UsoDeArmazenamento(int Quantidade, long Bytes);

/// <summary>
/// Pedido de envio de arquivo.
/// </summary>
/// <remarks>
/// Não existe campo de <c>ContentType</c> aqui de propósito: o tipo declarado pelo cliente no
/// multipart é texto arbitrário, e guardá-lo significaria devolvê-lo no download exatamente como
/// veio. Quem decide o tipo é o service, a partir da extensão — que já passou pela lista de
/// permissão.
/// </remarks>
/// <param name="Nome">Nome original informado pelo cliente.</param>
/// <param name="Tamanho">Tamanho em bytes.</param>
/// <param name="Conteudo">Fluxo com os bytes. Quem chama é dono do descarte.</param>
/// <param name="Categoria">Agrupamento lógico.</param>
public sealed record NovoArquivo(string Nome, long Tamanho, Stream Conteudo, string Categoria);

/// <summary>
/// Arquivo pronto para ser devolvido ao cliente.
/// </summary>
/// <param name="Conteudo">Fluxo com os bytes. Quem chama é dono do descarte.</param>
/// <param name="Nome">Nome original, usado no cabeçalho de download.</param>
/// <param name="ContentType">Tipo do conteúdo.</param>
public sealed record ArquivoParaDownload(Stream Conteudo, string Nome, string ContentType);
