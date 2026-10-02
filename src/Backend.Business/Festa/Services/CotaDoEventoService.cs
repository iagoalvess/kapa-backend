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
/// A cota de convites de um evento: o número por formando, a capacidade do local e a abertura.
/// </summary>
/// <remarks>
/// A colação (Sprint 30) e a festa (01/10/2026). Só a fonte do direito: o convite que sai daqui é o
/// mesmo da Sprint 21, com <c>PedidoId</c> nulo — página, PDF, check-in, lista e revogação não sabem
/// que ele veio de cota (decisão 2). Sem dinheiro em lugar nenhum (decisão 4).
/// </remarks>
/// <param name="agenda">O evento da cota.</param>
/// <param name="convites">A conta do painel.</param>
/// <param name="emissao">A emissão idempotente da cota.</param>
/// <param name="eventos">Auditoria da abertura.</param>
/// <param name="validator">Forma da cota.</param>
/// <param name="formaturaAtual">Turma da sessão.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
public sealed class CotaDoEventoService(
    IEventoDaTurmaRepository agenda,
    IConviteDoEventoRepository convites,
    EmissaoDeConvites emissao,
    IEventoRepository eventos,
    IValidator<DadosDaCota> validator,
    IFormaturaAtual formaturaAtual,
    IUnitOfWork unitOfWork
) : ICotaDoEventoService
{
    private static readonly Erro SemEvento = Erro.NaoEncontrado("agenda.evento_nao_encontrado", "Evento não encontrado na agenda.");

    /// <summary>Só a festa e a colação têm convite — e são os únicos tipos únicos por turma.</summary>
    private static readonly Erro SemConvite = Erro.Validacao("festa.evento_sem_convite", "Só a festa e a colação têm cota de convites.");

    private static readonly Erro NaoConfigurada = Erro.Conflito(
        "festa.cota_nao_configurada",
        "Defina quantos convites cada formando recebe antes de abrir a cota."
    );

    /// <inheritdoc />
    public async Task<Result<PainelDaCota>> Obter(TipoDeEvento tipo, CancellationToken ct = default) =>
        !TemConvite(tipo) ? SemConvite
        : await Evento(tipo, ct) is { } evento ? await Painel(evento, ct)
        : SemEvento;

    /// <inheritdoc />
    public async Task<Result<PainelDaCota>> Definir(TipoDeEvento tipo, DadosDaCota dados, CancellationToken ct = default)
    {
        if (!TemConvite(tipo))
            return SemConvite;

        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PainelDaCota>(validacao.Erros);

        if (await Evento(tipo, ct) is not { } evento)
            return SemEvento;

        var definida = evento.DefinirCota(dados.CotaPorFormando, dados.Capacidade);
        if (definida.Falhou)
            return Result.Falha<PainelDaCota>(definida.Erros);

        await unitOfWork.SalvarAsync(ct);

        return await Painel(evento, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A abertura é salva antes da emissão: a instrução da emissão lê <c>cota_aberta_em</c> do banco,
    /// e é a mesma que a entrada na turma usa depois. Ela abrange todo evento com cota aberta, o que a
    /// torna idempotente também quando as duas cotas já existem.
    /// </remarks>
    public async Task<Result<PainelDaCota>> Abrir(TipoDeEvento tipo, Guid usuarioId, CancellationToken ct = default)
    {
        if (!TemConvite(tipo))
            return SemConvite;

        var formaturaId = formaturaAtual.Id ?? throw new InvalidOperationException("Abertura de cota sem formatura selecionada na sessão.");

        var aberta = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                if (await Evento(tipo, token) is not { } evento)
                    return Result.Falha<EventoDaTurma>(SemEvento);

                if (evento.CotaPorFormando is not { } cota)
                    return Result.Falha<EventoDaTurma>(NaoConfigurada);

                var completo = EmissaoDeConvites.Completo(ParaResumo(evento));
                if (completo.Falhou)
                    return Result.Falha<EventoDaTurma>(completo.Erros);

                evento.AbrirCota(DateTime.UtcNow);
                await unitOfWork.SalvarAsync(token);

                var emitidos = await emissao.EmitirDaCota(formaturaId, null, token);

                await eventos.Auditar(
                    NomesDeAuditoria.CotaAberta,
                    usuarioId,
                    new
                    {
                        formaturaId,
                        eventoId = evento.Id,
                        tipo,
                        cotaPorFormando = cota,
                        emitidos,
                    },
                    token
                );

                await unitOfWork.SalvarAsync(token);

                return Result.Ok(evento);
            },
            ct
        );

        return aberta.Falhou ? Result.Falha<PainelDaCota>(aberta.Erros) : await Painel(aberta.Valor, ct);
    }

    /// <summary>O evento rastreado; nulo se a turma ainda não o marcou na agenda.</summary>
    private async Task<EventoDaTurma?> Evento(TipoDeEvento tipo, CancellationToken ct) =>
        await agenda.ObterDoTipo(tipo, ct) is { } resumo ? await agenda.ObterParaEdicao(resumo.Id, ct) : null;

    /// <summary>Festa e colação são os únicos eventos com convite — e os únicos únicos por turma.</summary>
    private static bool TemConvite(TipoDeEvento tipo) => tipo is TipoDeEvento.Festa or TipoDeEvento.Colacao;

    /// <summary>A conta aberta: <c>cota × formandos ativos + cortesias</c> contra a capacidade (decisão 3 e P3).</summary>
    private async Task<PainelDaCota> Painel(EventoDaTurma evento, CancellationToken ct)
    {
        var contagem = await convites.ContarDaCota(evento.Id, ct);
        var lugares = (evento.CotaPorFormando ?? 0) * contagem.FormandosAtivos + contagem.Cortesias;

        return new PainelDaCota(
            EmissaoDeConvites.ParaConvite(ParaResumo(evento)),
            evento.CotaPorFormando,
            evento.Capacidade,
            evento.CotaAbertaEm,
            contagem.FormandosAtivos,
            contagem.Cortesias,
            lugares,
            evento.Capacidade is { } capacidade ? Math.Max(0, lugares - capacidade) : 0,
            contagem.Emitidos,
            contagem.Nomeados,
            contagem.Emitidos - contagem.Nomeados
        );
    }

    private static EventoResumo ParaResumo(EventoDaTurma evento) =>
        new(evento.Id, evento.Titulo, evento.Tipo, evento.Situacao, evento.Data, evento.Hora, evento.Local, evento.Descricao);
}
