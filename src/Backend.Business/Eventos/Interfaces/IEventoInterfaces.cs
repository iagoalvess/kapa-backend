using Backend.Business.Abstractions;
using Backend.Business.Eventos.Models;

namespace Backend.Business.Eventos.Interfaces;

/// <summary>
/// Registra um evento de uso.
/// </summary>
/// <remarks>
/// A implementação enfileira em memória e devolve na hora. **Nunca bloqueia e nunca lança** —
/// analytics não pode derrubar nem atrasar a operação que estava sendo medida. Se a fila estiver
/// cheia, o evento é descartado e o descarte é contabilizado.
/// <para>
/// Essa é a diferença entre este canal e o de e-mail: e-mail perdido é problema do usuário,
/// evento perdido é um ponto a menos num gráfico.
/// </para>
/// </remarks>
public interface IRegistradorDeEventos
{
    /// <summary>Registra um evento para gravação assíncrona.</summary>
    /// <param name="evento">Evento a registrar.</param>
    void Registrar(Evento evento);
}

/// <summary>
/// Acesso à tabela de eventos.
/// </summary>
public interface IEventoRepository
{
    /// <summary>
    /// Grava um lote de eventos.
    /// </summary>
    /// <remarks>
    /// Em lote porque evento é barato de produzir e caro de gravar um a um: uma ida ao banco por
    /// clique não escala. A gravação acontece fora do caminho da requisição.
    /// </remarks>
    /// <param name="eventos">Eventos a gravar.</param>
    Task GravarLote(IReadOnlyList<Evento> eventos, CancellationToken ct = default);

    /// <summary>
    /// Marca um evento para gravar junto com a operação que o originou, no <c>SalvarAsync</c> dela.
    /// </summary>
    /// <remarks>
    /// Para auditoria, e não para analytics: a troca da chave PIX não pode acontecer sem deixar
    /// rastro, e a fila de <see cref="IRegistradorDeEventos"/> descarta quando está cheia. Aqui o
    /// evento e a troca ficam os dois, ou nenhum.
    /// </remarks>
    /// <param name="evento">Evento a gravar.</param>
    Task Adicionar(Evento evento, CancellationToken ct = default);

    /// <summary>
    /// Apaga eventos anteriores ao limite.
    /// </summary>
    /// <remarks>
    /// Chamado pelo worker de retenção. Sem isso a tabela cresce para sempre — e é a tabela que
    /// mais cresce, porque registra atividade e não estado.
    /// </remarks>
    /// <param name="limiteUtc">Só remove eventos anteriores a este instante.</param>
    /// <param name="limiteDaAuditoriaUtc">
    /// Limite aplicado aos nomes de <see cref="NomesDeAuditoria"/>, que vivem mais (decisão 3 da
    /// Sprint 14). Nulo aplica o mesmo limite a todos.
    /// </param>
    /// <returns>Quantidade de eventos removidos.</returns>
    Task<int> RemoverAnterioresA(DateTime limiteUtc, DateTime? limiteDaAuditoriaUtc = null, CancellationToken ct = default);

    /// <summary>
    /// A trilha de auditoria de uma formatura, paginada, do mais recente para o mais antigo.
    /// </summary>
    /// <remarks>
    /// A tabela de eventos <b>não tem coluna de formatura</b>: ela é de toda a plataforma, e a turma
    /// vai no corpo JSON de cada evento auditável. O recorte, portanto, é por
    /// <c>dados-&gt;&gt;'formaturaId'</c> — e é por isso que este método recebe a formatura em vez de
    /// confiar no filtro global, que aqui não existe.
    /// </remarks>
    /// <param name="formaturaId">Turma.</param>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Recorte pedido pela tela.</param>
    Task<PaginaDe<LinhaDeAuditoria>> ListarAuditoria(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeAuditoria filtro,
        CancellationToken ct = default
    );

    /// <summary>Autores e nomes de evento que a turma tem registrados, para os seletores da tela.</summary>
    /// <param name="formaturaId">Turma.</param>
    Task<OpcoesDeAuditoria> OpcoesDeAuditoria(Guid formaturaId, CancellationToken ct = default);

    /// <summary>Os números do topo da tela de Auditoria, da turma inteira e sem filtro.</summary>
    /// <param name="formaturaId">Turma.</param>
    /// <param name="recentesDesde">Início da janela dos eventos recentes.</param>
    Task<ResumoDaAuditoria> ResumirAuditoria(Guid formaturaId, DateTime recentesDesde, CancellationToken ct = default);
}

/// <summary>
/// A trilha de auditoria visível: quem fez o quê com o dinheiro da turma.
/// </summary>
/// <remarks>
/// Só leitura. A gravação acontece dentro da transação de cada operação auditada, por
/// <c>IEventoRepository.Auditar</c> — não existe, e não deve existir, um método aqui que escreva:
/// auditoria que a aplicação sabe escrever sob demanda é auditoria que ela sabe forjar.
/// </remarks>
public interface IAuditoriaService
{
    /// <summary>A trilha da turma, paginada e filtrada.</summary>
    /// <param name="formaturaId">Turma.</param>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Recorte pedido.</param>
    Task<Result<PaginaDe<LinhaDeAuditoria>>> Listar(
        Guid formaturaId,
        PaginacaoRequest paginacao,
        FiltroDeAuditoria filtro,
        CancellationToken ct = default
    );

    /// <summary>O que os seletores de filtro da tela oferecem.</summary>
    /// <param name="formaturaId">Turma.</param>
    Task<Result<OpcoesDeAuditoria>> Opcoes(Guid formaturaId, CancellationToken ct = default);

    /// <summary>Os números do topo da tela: quantas ações, quando foi a última e quem mais fez.</summary>
    /// <param name="formaturaId">Turma.</param>
    Task<Result<ResumoDaAuditoria>> Resumir(Guid formaturaId, CancellationToken ct = default);
}
