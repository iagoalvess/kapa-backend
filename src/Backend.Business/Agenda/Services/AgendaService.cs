using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Business.Common.Datas;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Agenda.Services;

/// <summary>
/// As datas da turma, num lugar só.
/// </summary>
/// <remarks>
/// Desde a Sprint 19 a agenda é a dona da colação e da festa (decisão 1): o que a
/// <c>FormaturaDetalhe</c> ainda devolve como <c>previsao_de_colacao</c> e <c>previsao_da_festa</c>
/// é projeção destes eventos, e é por isso que mover a data aqui muda o contador do Início e a
/// janela da projeção do caixa sem nenhuma outra escrita.
/// <para>
/// Nada aqui gera despesa, parcela, convite ou aviso, e nenhum e-mail sai daqui (decisão 9).
/// </para>
/// </remarks>
/// <param name="eventos">Eventos da turma.</param>
/// <param name="validator">Forma do evento.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class AgendaService(
    IEventoDaTurmaRepository eventos,
    IValidator<DadosDoEvento> validator,
    IUnitOfWork unitOfWork,
    ILogger<AgendaService> logger
) : IAgendaService
{
    /// <summary>Quantas datas a Página Inicial mostra: as três seguintes, e o resto vira contagem.</summary>
    private const int ProximosNaHome = 3;

    private static readonly Erro NaoEncontrado = Erro.NaoEncontrado("agenda.evento_nao_encontrado", "Evento não encontrado na agenda.");

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EventoResumo>>> Listar(CancellationToken ct = default) => Result.Ok(await eventos.Listar(ct));

    /// <inheritdoc />
    public async Task<Result<EventoResumo>> ObterPorId(Guid id, CancellationToken ct = default) =>
        await eventos.Obter(id, ct) is { } evento ? evento : NaoEncontrado;

    /// <inheritdoc />
    /// <remarks>
    /// Duas consultas, e não uma lista inteira cortada na memória: a home pede isto em toda
    /// abertura do app, e o que ela desenha são três linhas mais um "e mais N".
    /// </remarks>
    public async Task<Result<ResumoDaAgenda>> Resumir(CancellationToken ct = default)
    {
        var hoje = DataUtils.Hoje();

        return new ResumoDaAgenda(await eventos.Proximos(hoje, ProximosNaHome, ct), await eventos.ContarDaqui(hoje, ct));
    }

    /// <inheritdoc />
    public async Task<Result<EventoResumo>> Criar(DadosDoEvento dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<EventoResumo>(validacao.Erros);

        if (await Repetido(dados.Tipo, null, ct) is { } repetido)
            return repetido;

        var evento = EventoDaTurma.Novo(dados);

        await eventos.Adicionar(evento, ct);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Evento {EventoId} marcado na agenda da turma.", evento.Id);

        return await ObterPorId(evento.Id, ct);
    }

    /// <inheritdoc />
    public async Task<Result<EventoResumo>> Atualizar(Guid id, DadosDoEvento dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<EventoResumo>(validacao.Erros);

        var evento = await eventos.ObterParaEdicao(id, ct);
        if (evento is null)
            return NaoEncontrado;

        if (await Repetido(dados.Tipo, id, ct) is { } repetido)
            return repetido;

        evento.Aplicar(dados);
        await unitOfWork.SalvarAsync(ct);

        return await ObterPorId(id, ct);
    }

    /// <inheritdoc />
    public async Task<Result> Excluir(Guid id, CancellationToken ct = default)
    {
        var evento = await eventos.ObterParaEdicao(id, ct);
        if (evento is null)
            return Result.Falha(NaoEncontrado);

        eventos.Remover(evento);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Evento {EventoId} removido da agenda da turma.", id);

        return Result.Ok();
    }

    /// <summary>
    /// O erro de segunda colação ou segunda festa, ou nulo quando o tipo aceita repetição.
    /// </summary>
    /// <remarks>
    /// Isto dá o <b>código</b> do erro, não a garantia: duas requisições simultâneas passam as duas
    /// por aqui, e quem impede a segunda de nascer é o índice único parcial do mapeamento — que o
    /// <c>ClassificadorDeExcecao</c> traduz em 409 do mesmo jeito (decisão 2).
    /// </remarks>
    /// <param name="tipo">Tipo do evento.</param>
    /// <param name="exceto">Evento em edição, que não conflita consigo mesmo.</param>
    private async Task<Erro?> Repetido(TipoDeEvento tipo, Guid? exceto, CancellationToken ct) =>
        TiposDeEvento.EhUnico(tipo) && await eventos.ExisteDoTipo(tipo, exceto, ct)
            ? Erro.Conflito(
                "agenda.tipo_unico",
                tipo == TipoDeEvento.Colacao
                    ? "Esta turma já tem uma colação na agenda. Altere a data da que existe."
                    : "Esta turma já tem uma festa na agenda. Altere a data da que existe."
            )
            : null;
}
