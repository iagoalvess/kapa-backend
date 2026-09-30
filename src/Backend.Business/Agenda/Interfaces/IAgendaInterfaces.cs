using Backend.Business.Abstractions;
using Backend.Business.Agenda.Models;

namespace Backend.Business.Agenda.Interfaces;

/// <summary>
/// As datas da turma, num lugar só.
/// </summary>
/// <remarks>
/// Leitura para todo membro, escrita para a Gestão (P3). Nada aqui gera despesa, parcela, convite
/// ou aviso: data é data (decisão 9).
/// </remarks>
public interface IAgendaService
{
    /// <summary>Todos os eventos da turma, do mais antigo para o mais novo.</summary>
    /// <remarks>Sem paginação e sem filtro: a agenda de uma formatura inteira cabe numa consulta (decisão 10).</remarks>
    Task<Result<IReadOnlyList<EventoResumo>>> Listar(CancellationToken ct = default);

    /// <summary>Um evento da turma.</summary>
    /// <param name="id">Evento.</param>
    Task<Result<EventoResumo>> ObterPorId(Guid id, CancellationToken ct = default);

    /// <summary>As próximas datas e quantas ainda vêm — o bloco da Página Inicial.</summary>
    /// <remarks>Cancelado não entra: ali o espaço é do que vai acontecer.</remarks>
    Task<Result<ResumoDaAgenda>> Resumir(CancellationToken ct = default);

    /// <summary>Marca uma data nova.</summary>
    /// <remarks>Colação e festa já marcadas devolvem 409 <c>agenda.tipo_unico</c>: a existente se move.</remarks>
    /// <param name="dados">Título, tipo, situação, dia, hora, local e descrição.</param>
    Task<Result<EventoResumo>> Criar(DadosDoEvento dados, CancellationToken ct = default);

    /// <summary>Corrige uma data — inclusive a de um evento cancelado, que é como se remarca.</summary>
    /// <param name="id">Evento.</param>
    /// <param name="dados">Dados novos.</param>
    Task<Result<EventoResumo>> Atualizar(Guid id, DadosDoEvento dados, CancellationToken ct = default);

    /// <summary>Tira o evento da agenda.</summary>
    /// <remarks>
    /// Excluir é para o que foi digitado errado; o que a turma desmarcou vira
    /// <see cref="SituacaoDoEvento.Cancelado"/> e fica na lista — a mesma regra do item da festa
    /// (Sprint 17, decisão 13). Nada depende de um evento, então não há o que barrar aqui.
    /// </remarks>
    /// <param name="id">Evento.</param>
    Task<Result> Excluir(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Os eventos da formatura selecionada.
/// </summary>
/// <remarks>Isolados pelo filtro global: nenhum método recebe a formatura.</remarks>
public interface IEventoDaTurmaRepository
{
    /// <summary>Os eventos, do mais antigo para o mais novo.</summary>
    Task<IReadOnlyList<EventoResumo>> Listar(CancellationToken ct = default);

    /// <summary>Um evento, como a lista o mostra; nulo se não existir aqui.</summary>
    /// <param name="id">Evento.</param>
    Task<EventoResumo?> Obter(Guid id, CancellationToken ct = default);

    /// <summary>As próximas datas que ainda vêm, da mais perto para a mais longe.</summary>
    /// <param name="hoje">Dia de hoje no fuso da turma. O que é hoje ainda conta.</param>
    /// <param name="limite">Quantas trazer.</param>
    Task<IReadOnlyList<EventoResumo>> Proximos(DateOnly hoje, int limite, CancellationToken ct = default);

    /// <summary>O evento único de um tipo — a festa, a colação —; nulo se a turma ainda não o marcou.</summary>
    /// <remarks>É como o convite da festa acha o que imprimir (Sprint 21, P6).</remarks>
    /// <param name="tipo">Tipo único.</param>
    Task<EventoResumo?> ObterDoTipo(TipoDeEvento tipo, CancellationToken ct = default);

    /// <summary>O evento rastreado para alteração; nulo se não existir aqui.</summary>
    /// <param name="id">Evento.</param>
    Task<EventoDaTurma?> ObterParaEdicao(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Se a turma já tem um evento deste tipo, ignorando um id.
    /// </summary>
    /// <remarks>
    /// Dá o código do erro; quem <b>garante</b> é o índice único parcial do mapeamento — duas
    /// requisições simultâneas passam as duas por aqui, e uma delas esbarra no banco.
    /// </remarks>
    /// <param name="tipo">Tipo único (colação ou festa).</param>
    /// <param name="exceto">Evento que está sendo editado, para ele não conflitar consigo mesmo.</param>
    Task<bool> ExisteDoTipo(TipoDeEvento tipo, Guid? exceto, CancellationToken ct = default);

    /// <summary>Marca um evento novo para inclusão.</summary>
    /// <param name="evento">Evento.</param>
    Task Adicionar(EventoDaTurma evento, CancellationToken ct = default);

    /// <summary>Marca o evento para exclusão.</summary>
    /// <param name="evento">Evento já carregado.</param>
    void Remover(EventoDaTurma evento);
}
