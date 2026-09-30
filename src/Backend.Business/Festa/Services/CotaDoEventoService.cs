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
/// A cota de convites da colação: o número por formando, a capacidade do auditório e a abertura.
/// </summary>
/// <remarks>
/// Só a fonte do direito (Sprint 30). O convite que sai daqui é o mesmo da Sprint 21, com
/// <c>PedidoId</c> nulo — página, PDF, check-in, lista e revogação não sabem que ele veio de cota
/// (decisão 2). Sem dinheiro em lugar nenhum (decisão 4).
/// </remarks>
/// <param name="agenda">A colação da agenda.</param>
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
    private static readonly Erro SemColacao = Erro.NaoEncontrado("agenda.evento_nao_encontrado", "A colação ainda não está na agenda.");

    private static readonly Erro NaoConfigurada = Erro.Conflito(
        "festa.cota_nao_configurada",
        "Defina quantos convites cada formando recebe antes de abrir a cota."
    );

    /// <inheritdoc />
    public async Task<Result<PainelDaCota>> Obter(CancellationToken ct = default) =>
        await Colacao(ct) is { } colacao ? await Painel(colacao, ct) : SemColacao;

    /// <inheritdoc />
    public async Task<Result<PainelDaCota>> Definir(DadosDaCota dados, CancellationToken ct = default)
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<PainelDaCota>(validacao.Erros);

        if (await Colacao(ct) is not { } colacao)
            return SemColacao;

        var definida = colacao.DefinirCota(dados.CotaPorFormando, dados.Capacidade);
        if (definida.Falhou)
            return Result.Falha<PainelDaCota>(definida.Erros);

        await unitOfWork.SalvarAsync(ct);

        return await Painel(colacao, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A abertura é salva antes da emissão: a instrução da emissão lê <c>cota_aberta_em</c> do banco,
    /// e é a mesma que a entrada na turma usa depois.
    /// </remarks>
    public async Task<Result<PainelDaCota>> Abrir(Guid usuarioId, CancellationToken ct = default)
    {
        var formaturaId = formaturaAtual.Id ?? throw new InvalidOperationException("Abertura de cota sem formatura selecionada na sessão.");

        var aberta = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                if (await Colacao(token) is not { } colacao)
                    return Result.Falha<EventoDaTurma>(SemColacao);

                if (colacao.CotaPorFormando is not { } cota)
                    return Result.Falha<EventoDaTurma>(NaoConfigurada);

                var completo = EmissaoDeConvites.Completo(ParaResumo(colacao));
                if (completo.Falhou)
                    return Result.Falha<EventoDaTurma>(completo.Erros);

                colacao.AbrirCota(DateTime.UtcNow);
                await unitOfWork.SalvarAsync(token);

                var emitidos = await emissao.EmitirDaCota(formaturaId, null, token);

                await eventos.Auditar(
                    NomesDeAuditoria.CotaAberta,
                    usuarioId,
                    new
                    {
                        formaturaId,
                        eventoId = colacao.Id,
                        cotaPorFormando = cota,
                        emitidos,
                    },
                    token
                );

                await unitOfWork.SalvarAsync(token);

                return Result.Ok(colacao);
            },
            ct
        );

        return aberta.Falhou ? Result.Falha<PainelDaCota>(aberta.Erros) : await Painel(aberta.Valor, ct);
    }

    /// <summary>A colação rastreada; nula se a turma ainda não a marcou.</summary>
    private async Task<EventoDaTurma?> Colacao(CancellationToken ct) =>
        await agenda.ObterDoTipo(TipoDeEvento.Colacao, ct) is { } resumo ? await agenda.ObterParaEdicao(resumo.Id, ct) : null;

    /// <summary>A conta aberta: <c>cota × formandos ativos + cortesias</c> contra a capacidade (decisão 3 e P3).</summary>
    private async Task<PainelDaCota> Painel(EventoDaTurma colacao, CancellationToken ct)
    {
        var contagem = await convites.ContarDaCota(colacao.Id, ct);
        var lugares = (colacao.CotaPorFormando ?? 0) * contagem.FormandosAtivos + contagem.Cortesias;

        return new PainelDaCota(
            EmissaoDeConvites.ParaConvite(ParaResumo(colacao)),
            colacao.CotaPorFormando,
            colacao.Capacidade,
            colacao.CotaAbertaEm,
            contagem.FormandosAtivos,
            contagem.Cortesias,
            lugares,
            colacao.Capacidade is { } capacidade ? Math.Max(0, lugares - capacidade) : 0,
            contagem.Emitidos,
            contagem.Nomeados,
            contagem.Emitidos - contagem.Nomeados
        );
    }

    private static EventoResumo ParaResumo(EventoDaTurma evento) =>
        new(evento.Id, evento.Titulo, evento.Tipo, evento.Situacao, evento.Data, evento.Hora, evento.Local, evento.Descricao);
}
