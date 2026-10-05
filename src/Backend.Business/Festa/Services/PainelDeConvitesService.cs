using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using FluentValidation;

namespace Backend.Business.Festa.Services;

/// <summary>
/// O painel de convites de um evento: a capacidade do local, a conta de lugares e os convites presos por atraso.
/// </summary>
/// <remarks>
/// Sucessor da cota da Sprint 30 (Sprint 47, D15): o número de convites deixou de ser um campo do evento e passou a
/// ser a soma dos benefícios das cestas. O convite que sai daqui continua o mesmo da Sprint 21, com <c>PedidoId</c>
/// nulo — página, PDF, check-in, lista e revogação não sabem de onde ele veio.
/// </remarks>
/// <param name="agenda">O evento.</param>
/// <param name="convites">A conta do painel e a trava dos convites a liberar.</param>
/// <param name="eventos">Auditoria da liberação.</param>
/// <param name="validator">Forma da capacidade.</param>
/// <param name="formaturaAtual">Turma da sessão.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
public sealed class PainelDeConvitesService(
    IEventoDaTurmaRepository agenda,
    IConviteDoEventoRepository convites,
    IEventoRepository eventos,
    IValidator<DadosDaCapacidade> validator,
    IFormaturaAtual formaturaAtual,
    IUnitOfWork unitOfWork
) : IPainelDeConvitesService
{
    private static readonly Erro SemEvento = Erro.NaoEncontrado("agenda.evento_nao_encontrado", "Evento não encontrado na agenda.");

    /// <summary>Só a festa e a colação têm convite — e são os únicos tipos únicos por turma.</summary>
    private static readonly Erro SemConvite = Erro.Validacao("festa.evento_sem_convite", "Só a festa e a colação têm convites.");

    /// <inheritdoc />
    public async Task<Result<PainelDeConvites>> Obter(TipoDeEvento tipo, CancellationToken ct = default) =>
        !TemConvite(tipo) ? SemConvite
        : await Evento(tipo, ct) is { } evento ? await Painel(evento, ct)
        : SemEvento;

    /// <inheritdoc />
    public async Task<Result<PainelDeConvites>> DefinirCapacidade(TipoDeEvento tipo, DadosDaCapacidade dados, CancellationToken ct = default)
    {
        if (!TemConvite(tipo))
            return SemConvite;

        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PainelDeConvites>(validacao.Erros);

        if (await Evento(tipo, ct) is not { } evento)
            return SemEvento;

        evento.DefinirCapacidade(dados.Capacidade);
        await unitOfWork.SalvarAsync(ct);

        return await Painel(evento, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Marca só os convites ainda válidos: o revogado não volta por liberação. Liberar de novo não muda nada e
    /// devolve zero — o clique duplo não gera duas auditorias com efeito.
    /// </remarks>
    public async Task<Result<int>> Liberar(Guid vinculoId, Guid usuarioId, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var aLiberar = (await convites.TravarDosPacotes(vinculoId, ct)).Where(convite => convite.LiberadoEm is null).ToList();

        if (aLiberar.Count == 0)
            return 0;

        foreach (var convite in aLiberar)
            convite.Liberar(agora);

        await eventos.Auditar(
            NomesDeAuditoria.ConvitesDesbloqueados,
            usuarioId,
            new
            {
                formaturaId = formaturaAtual.Id,
                vinculoId,
                convites = aLiberar.Count,
            },
            ct
        );

        await unitOfWork.SalvarAsync(ct);

        return aLiberar.Count;
    }

    /// <summary>O evento rastreado; nulo se a turma ainda não o marcou na agenda.</summary>
    private async Task<EventoDaTurma?> Evento(TipoDeEvento tipo, CancellationToken ct) =>
        await agenda.ObterDoTipo(tipo, ct) is { } resumo ? await agenda.ObterParaEdicao(resumo.Id, ct) : null;

    /// <summary>Festa e colação são os únicos eventos com convite — e os únicos únicos por turma.</summary>
    private static bool TemConvite(TipoDeEvento tipo) => tipo is TipoDeEvento.Festa or TipoDeEvento.Colacao;

    /// <summary>A conta aberta: benefícios + extras + cortesias contra a capacidade.</summary>
    private async Task<PainelDeConvites> Painel(EventoDaTurma evento, CancellationToken ct)
    {
        var contagem = await convites.ContarDoPainel(evento.Id, evento.Tipo, ct);
        var lugares = contagem.Beneficios + contagem.Extras + contagem.Cortesias;

        return new PainelDeConvites(
            EmissaoDeConvites.ParaConvite(ParaResumo(evento)),
            evento.Capacidade,
            contagem.FormandosAtivos,
            contagem.Beneficios,
            contagem.Extras,
            contagem.Cortesias,
            lugares,
            evento.Capacidade is { } capacidade ? Math.Max(0, lugares - capacidade) : 0,
            contagem.Emitidos,
            contagem.Nomeados,
            contagem.Emitidos - contagem.Nomeados,
            await convites.ListarPresos(evento.Id, ct)
        );
    }

    private static EventoResumo ParaResumo(EventoDaTurma evento) =>
        new(evento.Id, evento.Titulo, evento.Tipo, evento.Situacao, evento.Data, evento.Hora, evento.Local, evento.Descricao);
}
