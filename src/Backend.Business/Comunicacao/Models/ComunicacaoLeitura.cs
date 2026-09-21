namespace Backend.Business.Comunicacao.Models;

/// <summary>O que a comissão escreve ao publicar ou corrigir um aviso.</summary>
/// <param name="Titulo">Título.</param>
/// <param name="Conteudo">Texto em markdown.</param>
/// <param name="Visibilidade">Para quem aparece. Obrigatória: nulo é recusado, nunca vira "turma".</param>
/// <param name="Fixado">Se fica no topo do mural.</param>
/// <param name="Destaque">Se leva o selo de importante.</param>
public sealed record DadosDoAviso(string Titulo, string Conteudo, Visibilidade? Visibilidade, bool Fixado, bool Destaque);

/// <summary>Um aviso, como o mural o mostra.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Conteudo">Texto em markdown, cru.</param>
/// <param name="Visibilidade">Para quem aparece.</param>
/// <param name="Fixado">Se fica no topo.</param>
/// <param name="Destaque">Se é importante.</param>
/// <param name="PublicadoEm">Quando foi publicado, em UTC.</param>
/// <param name="AtualizadoEm">Última correção, em UTC — igual a <paramref name="PublicadoEm"/> se nunca foi corrigido.</param>
/// <param name="PublicadoPorUsuarioId">Quem publicou.</param>
/// <param name="Autor">Nome de quem publicou; nulo se a conta não existir mais.</param>
public sealed record AvisoResumo(
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

/// <summary>Filtros do mural.</summary>
/// <param name="Fixado">Só os fixados no topo.</param>
/// <param name="Destaque">Só os marcados como importantes.</param>
/// <param name="Visibilidade">Só os desta visibilidade — o filtro "só comissão" da tela.</param>
/// <param name="De">Publicados a partir deste dia, inclusive, no fuso de exibição.</param>
/// <param name="Ate">Publicados até este dia, inclusive, no fuso de exibição.</param>
/// <param name="Busca">Trecho do título ou do texto, sem diferenciar acento.</param>
public sealed record FiltroDeAvisos(
    bool? Fixado = null,
    bool? Destaque = null,
    Visibilidade? Visibilidade = null,
    DateOnly? De = null,
    DateOnly? Ate = null,
    string? Busca = null
);

/// <summary>O mural em números, dentro do que quem consulta pode ver.</summary>
/// <remarks>São as contagens das pílulas da tela: cada uma é o total do filtro, e não da página.</remarks>
/// <param name="Quantidade">Avisos.</param>
/// <param name="Fixados">Quantos estão fixados — o teto é <see cref="Aviso.LimiteDeFixados"/>.</param>
/// <param name="Importantes">Quantos levam o selo de importante.</param>
/// <param name="Internos">Quantos são só da comissão; zero para quem não os vê.</param>
/// <param name="UltimaPublicacao">A publicação mais recente, em UTC; nula com o mural vazio.</param>
public sealed record ResumoDoMural(int Quantidade, int Fixados, int Importantes, int Internos, DateTime? UltimaPublicacao);

/// <summary>O que a comissão preenche ao enviar ou corrigir um documento.</summary>
/// <param name="Titulo">Como a turma chama o documento.</param>
/// <param name="Categoria">Gaveta do acervo. Obrigatória.</param>
/// <param name="Visibilidade">Para quem aparece. Obrigatória.</param>
public sealed record DadosDoDocumento(string Titulo, CategoriaDeDocumento? Categoria, Visibilidade? Visibilidade);

/// <summary>
/// Filtros do acervo.
/// </summary>
/// <remarks>
/// <see cref="Visibilidade"/> <b>estreita</b> o que o papel já podia ver, nunca alarga: quem não vê
/// documento interno e pedir <c>SomenteComissao</c> recebe lista vazia, e não o acervo da comissão.
/// Existe para quem precisa escolher um documento que a turma inteira abre — o contrato de um item
/// da festa (Sprint 17, decisão 7) —, sem filtrar depois da paginação e acabar com uma lista vazia
/// por acidente.
/// </remarks>
/// <param name="Categoria">Só desta categoria.</param>
/// <param name="Busca">Trecho do título, sem diferenciar acento.</param>
/// <param name="Visibilidade">Só os desta visibilidade, dentro do que o papel já enxerga.</param>
public sealed record FiltroDeDocumentos(CategoriaDeDocumento? Categoria = null, string? Busca = null, Visibilidade? Visibilidade = null);

/// <summary>Um documento, como o acervo o lista.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Titulo">Título.</param>
/// <param name="Categoria">Gaveta.</param>
/// <param name="Visibilidade">Para quem aparece.</param>
/// <param name="Versao">Versão do arquivo atual.</param>
/// <param name="NomeDoArquivo">Nome original do arquivo atual.</param>
/// <param name="ContentType">Tipo do arquivo atual.</param>
/// <param name="Tamanho">Tamanho do arquivo atual, em bytes.</param>
/// <param name="EnviadoEm">Quando o arquivo atual foi enviado, em UTC.</param>
/// <param name="EnviadoPor">Quem enviou o arquivo atual; nulo se a conta não existir mais.</param>
public sealed record DocumentoResumo(
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
public sealed record DocumentosNaCategoria(CategoriaDeDocumento Categoria, int Quantidade);

/// <summary>O acervo em números, dentro do que quem consulta pode ver.</summary>
/// <param name="Quantidade">Documentos.</param>
/// <param name="Bytes">Soma do tamanho dos arquivos atuais.</param>
/// <param name="UltimoEnvio">O envio mais recente, em UTC; nulo com o acervo vazio.</param>
/// <param name="PorCategoria">Quantidade por categoria — só as que têm algum.</param>
public sealed record ResumoDoAcervo(int Quantidade, long Bytes, DateTime? UltimoEnvio, IReadOnlyList<DocumentosNaCategoria> PorCategoria);

/// <summary>O arquivo atual de um documento, para baixar ou apagar como quem o enviou.</summary>
/// <param name="ArquivoId">Arquivo no módulo de arquivos.</param>
/// <param name="EnviadoPorId">Quem enviou — o dono do arquivo lá.</param>
/// <param name="Nome">Nome original, para a auditoria.</param>
public sealed record ArquivoDoDocumento(Guid ArquivoId, Guid EnviadoPorId, string Nome);

/// <summary>Um aviso no sino: o mínimo para reconhecê-lo e abri-lo.</summary>
/// <param name="Id">Aviso.</param>
/// <param name="Titulo">Como ele aparece na lista.</param>
/// <param name="PublicadoEm">Quando entrou no mural, em UTC.</param>
/// <param name="Destaque">Se a comissão o marcou como importante.</param>
public sealed record NovidadeDoMural(Guid Id, string Titulo, DateTime PublicadoEm, bool Destaque);

/// <summary>
/// O que há de novo no mural desde a última visita — o conteúdo do sino.
/// </summary>
/// <remarks>
/// A conta é sobre <c>VinculoDeFormatura.MuralVistoEm</c>: novo é o que foi publicado depois dela.
/// Quem nunca abriu o mural vê tudo como novo, que é o certo para quem acabou de entrar na turma.
/// <para>
/// A lista traz só os primeiros; a <see cref="Quantidade"/> é a de todos, e é ela que vai no selo.
/// Trinta avisos novos não viram trinta linhas num balão de cabeçalho.
/// </para>
/// </remarks>
/// <param name="Quantidade">Quantos avisos novos há ao todo.</param>
/// <param name="Itens">Os mais recentes, do mais novo para o mais antigo.</param>
public sealed record NovidadesDoMural(int Quantidade, IReadOnlyList<NovidadeDoMural> Itens)
{
    /// <summary>Nada novo — e nada a mostrar.</summary>
    public static NovidadesDoMural Nenhuma => new(0, []);
}
