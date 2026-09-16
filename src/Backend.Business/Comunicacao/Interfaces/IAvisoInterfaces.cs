using Backend.Business.Abstractions;
using Backend.Business.Comunicacao.Models;

namespace Backend.Business.Comunicacao.Interfaces;

/// <summary>
/// O mural da turma: a comissão publica, todo membro lê.
/// </summary>
/// <remarks>
/// Toda leitura recebe quem consulta, e não um "incluir internos": o service descobre o papel no
/// vínculo gravado e o repositório filtra por ele. Aviso <see cref="Visibilidade.SomenteComissao"/>
/// nunca chega à resposta de um formando — nem na lista, nem pelo id (404, como o que não existe).
/// </remarks>
public interface IAvisoService
{
    /// <summary>Uma página do mural: fixados primeiro, depois do mais novo para o mais antigo.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem consulta.</param>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Fixados, importantes, visibilidade e busca.</param>
    Task<Result<PaginaDe<AvisoResumo>>> Listar(
        Guid formaturaId,
        Guid usuarioId,
        PaginacaoRequest paginacao,
        FiltroDeAvisos filtro,
        CancellationToken ct = default
    );

    /// <summary>O mural em números, para os indicadores e as contagens das pílulas.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem consulta.</param>
    Task<Result<ResumoDoMural>> Resumir(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Um aviso, se quem consulta pode lê-lo.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem consulta.</param>
    /// <param name="id">Aviso.</param>
    Task<Result<AvisoResumo>> Obter(Guid formaturaId, Guid usuarioId, Guid id, CancellationToken ct = default);

    /// <summary>Publica um aviso. O quarto fixado devolve 409 <c>comunicacao.limite_de_fixados</c>.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem publica — o autor.</param>
    /// <param name="dados">Título, texto, visibilidade, fixado e destaque.</param>
    Task<Result<AvisoResumo>> Publicar(Guid formaturaId, Guid usuarioId, DadosDoAviso dados, CancellationToken ct = default);

    /// <summary>Corrige um aviso. Fixar um quarto devolve 409 <c>comunicacao.limite_de_fixados</c>.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem corrige.</param>
    /// <param name="id">Aviso.</param>
    /// <param name="dados">Dados novos.</param>
    Task<Result<AvisoResumo>> Atualizar(Guid formaturaId, Guid usuarioId, Guid id, DadosDoAviso dados, CancellationToken ct = default);

    /// <summary>Exclui um aviso, registrando na auditoria quem excluiu.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem exclui.</param>
    /// <param name="id">Aviso.</param>
    Task<Result> Excluir(Guid formaturaId, Guid usuarioId, Guid id, CancellationToken ct = default);
}

/// <summary>
/// Avisos da formatura selecionada.
/// </summary>
/// <remarks>
/// O filtro de visibilidade mora <b>aqui</b>, a partir do papel de quem consulta — não no controller,
/// e nem no front: filtro de visibilidade na tela é documento interno chegando na resposta da API
/// para quem sabe abrir o DevTools. Isoladas pelo filtro global: nenhum método recebe a formatura.
/// </remarks>
public interface IAvisoRepository
{
    /// <summary>Uma página do mural, fixados primeiro e depois do mais novo para o mais antigo.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Fixados, importantes, visibilidade e busca.</param>
    /// <param name="papel">Papel de quem consulta; fora da gestão, só os da turma.</param>
    Task<PaginaDe<AvisoResumo>> Listar(PaginacaoRequest paginacao, FiltroDeAvisos filtro, string? papel, CancellationToken ct = default);

    /// <summary>Quantos avisos, fixados, importantes e internos o papel vê, e a última publicação.</summary>
    /// <param name="papel">Papel de quem consulta.</param>
    Task<ResumoDoMural> Resumir(string? papel, CancellationToken ct = default);

    /// <summary>Um aviso; nulo se não existir aqui ou se o papel não o puder ler.</summary>
    /// <param name="id">Aviso.</param>
    /// <param name="papel">Papel de quem consulta.</param>
    Task<AvisoResumo?> Obter(Guid id, string? papel, CancellationToken ct = default);

    /// <summary>O aviso rastreado para alteração; nulo se não existir aqui.</summary>
    /// <param name="id">Aviso.</param>
    Task<Aviso?> ObterParaEdicao(Guid id, CancellationToken ct = default);

    /// <summary>Quantos avisos estão fixados agora.</summary>
    Task<int> ContarFixados(CancellationToken ct = default);

    /// <summary>Marca um aviso novo para inclusão.</summary>
    /// <param name="aviso">Aviso.</param>
    Task Adicionar(Aviso aviso, CancellationToken ct = default);

    /// <summary>Marca um aviso para remoção.</summary>
    /// <param name="aviso">Aviso rastreado.</param>
    void Remover(Aviso aviso);
}
