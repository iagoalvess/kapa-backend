using Backend.Business.Comunicacao.Models;

namespace Backend.Api.DTOs.Comunicacao;

/// <summary>Corpo da publicação e da correção de um aviso.</summary>
/// <param name="Titulo">Título.</param>
/// <param name="Conteudo">Texto em markdown.</param>
/// <param name="Visibilidade">Para quem aparece. Ausente, o validador recusa — nunca vira "turma".</param>
/// <param name="Fixado">Se fica no topo do mural.</param>
/// <param name="Destaque">Se leva o selo de importante.</param>
public sealed record AvisoRequestDTO(string? Titulo, string? Conteudo, Visibilidade? Visibilidade, bool Fixado, bool Destaque);

/// <summary>Um aviso do mural.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Conteudo">Texto em markdown, cru — quem renderiza sanitiza.</param>
/// <param name="Visibilidade">Para quem aparece.</param>
/// <param name="Fixado">Se fica no topo.</param>
/// <param name="Destaque">Se é importante.</param>
/// <param name="PublicadoEm">Quando foi publicado, em UTC.</param>
/// <param name="AtualizadoEm">Última correção, em UTC.</param>
/// <param name="PublicadoPorUsuarioId">Quem publicou.</param>
/// <param name="Autor">Nome de quem publicou; ausente se a conta não existir mais.</param>
public sealed record AvisoDTO(
    Guid Id,
    string Titulo,
    string Conteudo,
    Visibilidade Visibilidade,
    bool Fixado,
    bool Destaque,
    DateTime PublicadoEm,
    DateTime AtualizadoEm,
    Guid PublicadoPorUsuarioId,
    string? Autor
);

/// <summary>O mural em números, dentro do que quem consulta pode ver.</summary>
/// <param name="Quantidade">Avisos.</param>
/// <param name="Fixados">Quantos estão fixados no topo.</param>
/// <param name="Importantes">Quantos levam o selo de importante.</param>
/// <param name="Internos">Quantos são só da comissão; zero para quem não os vê.</param>
/// <param name="UltimaPublicacao">A publicação mais recente, em UTC; ausente com o mural vazio.</param>
public sealed record ResumoDoMuralDTO(int Quantidade, int Fixados, int Importantes, int Internos, DateTime? UltimaPublicacao);

/// <summary>Um aviso no sino.</summary>
/// <param name="Id">Aviso.</param>
/// <param name="Titulo">Como ele aparece na lista.</param>
/// <param name="PublicadoEm">Quando entrou no mural, em UTC.</param>
/// <param name="Destaque">Se a comissão o marcou como importante.</param>
public sealed record NovidadeDoMuralDTO(Guid Id, string Titulo, DateTime PublicadoEm, bool Destaque);

/// <summary>O que há de novo no mural desde a última visita de quem perguntou.</summary>
/// <param name="Quantidade">Quantos avisos novos há ao todo — é o número do selo.</param>
/// <param name="Itens">Os mais recentes, do mais novo para o mais antigo.</param>
public sealed record NovidadesDoMuralDTO(int Quantidade, IReadOnlyList<NovidadeDoMuralDTO> Itens);

/// <summary>Campos do envio e da correção de um documento (multipart, com o arquivo).</summary>
/// <param name="Titulo">Como a turma chama o documento.</param>
/// <param name="Categoria">Gaveta do acervo. Obrigatória.</param>
/// <param name="Visibilidade">Para quem aparece. Obrigatória.</param>
public sealed record DocumentoRequestDTO(string? Titulo, CategoriaDeDocumento? Categoria, Visibilidade? Visibilidade);

/// <summary>Um documento do acervo.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Categoria">Gaveta.</param>
/// <param name="Visibilidade">Para quem aparece.</param>
/// <param name="Versao">Versão do arquivo atual.</param>
/// <param name="NomeDoArquivo">Nome original do arquivo atual.</param>
/// <param name="ContentType">Tipo do arquivo atual.</param>
/// <param name="Tamanho">Tamanho em bytes.</param>
/// <param name="EnviadoEm">Quando o arquivo atual foi enviado, em UTC.</param>
/// <param name="EnviadoPor">Quem enviou; ausente se a conta não existir mais.</param>
public sealed record DocumentoDTO(
    Guid Id,
    string Titulo,
    CategoriaDeDocumento Categoria,
    Visibilidade Visibilidade,
    int Versao,
    string NomeDoArquivo,
    string ContentType,
    long Tamanho,
    DateTime EnviadoEm,
    string? EnviadoPor
);

/// <summary>Quantos documentos há numa categoria.</summary>
/// <param name="Categoria">Gaveta.</param>
/// <param name="Quantidade">Documentos nela.</param>
public sealed record DocumentosNaCategoriaDTO(CategoriaDeDocumento Categoria, int Quantidade);

/// <summary>O acervo em números, dentro do que quem consulta pode ver.</summary>
/// <param name="Quantidade">Documentos.</param>
/// <param name="Bytes">Soma do tamanho dos arquivos atuais.</param>
/// <param name="UltimoEnvio">O envio mais recente, em UTC; ausente com o acervo vazio.</param>
/// <param name="PorCategoria">Quantidade por categoria — só as que têm algum.</param>
public sealed record ResumoDoAcervoDTO(int Quantidade, long Bytes, DateTime? UltimoEnvio, IReadOnlyList<DocumentosNaCategoriaDTO> PorCategoria);
