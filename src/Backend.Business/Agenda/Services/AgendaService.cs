using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Formaturas.Interfaces;
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
/// Nada aqui gera despesa, parcela, convite ou aviso, e nenhum e-mail sai daqui (decisão 9). A exceção é
/// cancelar: evento cancelado não tem porta, e os convites que sobraram nele são revogados (Sprint 38, P10).
/// </para>
/// </remarks>
/// <param name="eventos">Eventos da turma.</param>
/// <param name="pendencias">As vendas de pé da festa, que impedem cancelá-la ou excluí-la (P10).</param>
/// <param name="convites">Os convites que o evento cancelado revoga.</param>
/// <param name="validator">Forma do evento.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class AgendaService(
    IEventoDaTurmaRepository eventos,
    IPendenciasDaTurmaRepository pendencias,
    IConviteDoEventoRepository convites,
    IValidator<DadosDoEvento> validator,
    IUnitOfWork unitOfWork,
    ILogger<AgendaService> logger
) : IAgendaService
{
    /// <summary>Quantas datas a Página Inicial mostra: as três seguintes, e o resto vira contagem.</summary>
    private const int ProximosNaHome = 3;

    /// <summary>O motivo que a portaria mostra no convite do evento cancelado.</summary>
    public const string MotivoDoCancelamento = "evento cancelado";

    private static readonly Erro NaoEncontrado = Erro.NaoEncontrado("agenda.evento_nao_encontrado", "Evento não encontrado na agenda.");

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EventoResumo>>> Listar(CancellationToken ct = default) => Result.Ok(await eventos.Listar(ct));

    /// <inheritdoc />
    public async Task<Result<EventoResumo>> ObterPorId(Guid id, CancellationToken ct = default) =>
        await eventos.Obter(id, ct) is { } evento ? evento : NaoEncontrado;

    /// <inheritdoc />
    /// <remarks>
    /// Recortada no banco, e não uma lista inteira cortada na memória: a home pede isto em toda
    /// abertura do app, e o que ela desenha são três linhas.
    /// </remarks>
    public async Task<Result<ResumoDaAgenda>> Resumir(CancellationToken ct = default) =>
        new ResumoDaAgenda(await eventos.Proximos(DataUtils.Hoje(), ProximosNaHome, ct));

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
    /// <remarks>
    /// Marcar a festa como cancelada com venda de pé responde 409 <c>agenda.evento_com_vendas</c> (Sprint 38, P10):
    /// o dinheiro se resolve primeiro, pelo caminho que devolve. Sem vendas, cancelar revoga o que sobrou sem
    /// dinheiro envolvido — cortesias e cota da colação —, na mesma transação.
    /// </remarks>
    public async Task<Result<EventoResumo>> Atualizar(Guid id, DadosDoEvento dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<EventoResumo>(validacao.Erros);

        var atualizado = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var evento = await eventos.ObterParaEdicao(id, token);
                if (evento is null)
                    return Result.Falha(NaoEncontrado);

                if (await Repetido(dados.Tipo, id, token) is { } repetido)
                    return Result.Falha(repetido);

                var cancelando = !evento.Cancelado && dados.Situacao == SituacaoDoEvento.Cancelado;

                if (cancelando && await ComVendas(evento.Tipo, token) is { } comVendas)
                    return Result.Falha(comVendas);

                evento.Aplicar(dados);

                if (cancelando)
                {
                    var agora = DateTime.UtcNow;
                    var revogados = (await convites.TravarValidosDoEvento(id, token)).Count(convite => convite.Revogar(MotivoDoCancelamento, agora));

                    logger.LogInformation("Evento {EventoId} cancelado; {Revogados} convites revogados.", id, revogados);
                }

                await unitOfWork.SalvarAsync(token);

                return Result.Ok();
            },
            ct
        );

        return atualizado.Falhou ? Result.Falha<EventoResumo>(atualizado.Erros) : await ObterPorId(id, ct);
    }

    /// <inheritdoc />
    public async Task<Result> Excluir(Guid id, CancellationToken ct = default)
    {
        var evento = await eventos.ObterParaEdicao(id, ct);
        if (evento is null)
            return Result.Falha(NaoEncontrado);

        if (await ComVendas(evento.Tipo, ct) is { } comVendas)
            return Result.Falha(comVendas);

        eventos.Remover(evento);
        await unitOfWork.SalvarAsync(ct);

        logger.LogInformation("Evento {EventoId} removido da agenda da turma.", id);

        return Result.Ok();
    }

    /// <summary>
    /// O 409 da festa com venda de pé, com as contagens em <c>dados</c> e o caminho na mensagem (P10); nulo nos
    /// outros eventos e na festa sem vendas.
    /// </summary>
    /// <param name="tipo">Tipo do evento — só a festa tem venda.</param>
    private async Task<Erro?> ComVendas(TipoDeEvento tipo, CancellationToken ct)
    {
        if (tipo != TipoDeEvento.Festa || await pendencias.ContarVendasDaFesta(ct) is not { Alguma: true } vendas)
            return null;

        var caminhos = new List<string>();
        if (vendas.ComprasDaLoja > 0)
            caminhos.Add("as compras da loja, em Loja > Cancelar as vendas da festa");
        if (vendas.PedidosDeConvite > 0)
            caminhos.Add("os pedidos de convite dos formandos, cancelados um a um em Pedidos");

        return new Erro("agenda.evento_com_vendas", $"A festa tem vendas de pé. Cancele antes {string.Join(" e ", caminhos)}.", ETipoErro.Conflito)
        {
            Dados = vendas,
        };
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
