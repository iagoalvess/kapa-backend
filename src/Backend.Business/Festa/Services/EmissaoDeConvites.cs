using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Formaturas.Interfaces;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Festa.Services;

/// <summary>
/// Emitir e revogar os convites de um pedido — o lado da festa do gatilho que mora no pedido.
/// </summary>
/// <remarks>
/// Quem decide <b>quando</b> é o <c>PedidoService</c>: quitação, estorno, redução e cancelamento
/// (P2, decisão 8). Aqui fica o <b>como</b>, para que a liberação manual da Gestão passe pelo mesmo
/// caminho. Não chama <c>SalvarAsync</c>: a emissão é uma instrução direta na transação de quem
/// chama, e a revogação marca entidades travadas que o chamador persiste.
/// <para>Sem interface, como <c>BaixaService</c>: uma implementação, e ninguém de fora a substitui.</para>
/// </remarks>
/// <param name="convites">Convites da turma.</param>
/// <param name="agenda">A festa da agenda (P6).</param>
/// <param name="formaturas">Curso e ano, para o prefixo do código.</param>
/// <param name="formaturaAtual">Turma da sessão.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class EmissaoDeConvites(
    IConviteDoEventoRepository convites,
    IEventoDaTurmaRepository agenda,
    IFormaturaRepository formaturas,
    IFormaturaAtual formaturaAtual,
    ILogger<EmissaoDeConvites> logger
)
{
    /// <summary>O motivo que a portaria mostra no convite de um pagamento desfeito.</summary>
    public const string MotivoDoEstorno = "pagamento estornado";

    /// <summary>O motivo do convite que sobrou depois de o pedido diminuir ou cair.</summary>
    public const string MotivoDoCancelamento = "pedido cancelado";

    /// <summary>O motivo do convite de cota de quem saiu da turma (Sprint 30, decisão 5).</summary>
    public const string MotivoDaSaida = "formando saiu da turma";

    /// <summary>A festa da agenda, se já dá para imprimir convite dela; senão, 409 <c>festa.evento_incompleto</c>.</summary>
    public async Task<Result<EventoDoConvite>> Festa(CancellationToken ct = default) => Completo(await agenda.ObterDoTipo(TipoDeEvento.Festa, ct));

    /// <summary>Um evento da agenda, se já dá para imprimir convite dele; senão, 409 <c>festa.evento_incompleto</c>.</summary>
    /// <param name="eventoId">Evento; nulo é a festa.</param>
    public async Task<Result<EventoDoConvite>> Evento(Guid? eventoId, CancellationToken ct = default) =>
        eventoId is { } id ? Completo(await agenda.Obter(id, ct)) : await Festa(ct);

    /// <summary>O evento como o convite o imprime, se tem hora e local (P6).</summary>
    /// <param name="evento">Evento lido da agenda; nulo se não existe.</param>
    public static Result<EventoDoConvite> Completo(EventoResumo? evento) =>
        evento is not null && ParaConvite(evento) is { Completo: true } completo ? completo : ErrosDoConvite.EventoIncompleto;

    /// <summary>O evento da agenda como o convite o imprime.</summary>
    /// <param name="evento">Evento da agenda.</param>
    public static EventoDoConvite ParaConvite(EventoResumo evento) =>
        new(evento.Id, evento.Tipo, evento.Titulo, evento.Data, evento.Hora, evento.Local);

    /// <summary>O prefixo dos códigos desta turma — <c>MED27</c>.</summary>
    public Task<string> Prefixo(CancellationToken ct = default) =>
        Prefixo(formaturaAtual.Id ?? throw new InvalidOperationException("Emissão de convite sem formatura selecionada na sessão."), ct);

    /// <summary>O prefixo dos códigos de uma turma qualquer — a entrada na turma ainda não tem a sessão dela.</summary>
    /// <param name="formaturaId">Turma.</param>
    public async Task<string> Prefixo(Guid formaturaId, CancellationToken ct = default)
    {
        var detalhe =
            await formaturas.ObterDetalheDeTodasAsFormaturas(formaturaId, ct)
            ?? throw new InvalidOperationException($"Formatura {formaturaId} não encontrada ao emitir convite.");

        return CodigoDoConvite.Prefixo(detalhe.Curso, detalhe.Ano);
    }

    /// <summary>
    /// Emite os convites 1 a N do pedido — de novo não cria nada (decisão 12).
    /// </summary>
    /// <param name="pedido">Pedido de convite extra.</param>
    /// <returns>Quantos convites do pedido valem; 409 se a festa não está completa na agenda.</returns>
    public async Task<Result<int>> EmitirDoPedido(Pedido pedido, CancellationToken ct = default)
    {
        var festa = await Festa(ct);
        if (festa.Falhou)
            return Result.Falha<int>(festa.Erros);

        var validos = await convites.EmitirDoPedido(festa.Valor.Id, pedido.VinculoId, pedido.Id, pedido.Quantidade, await Prefixo(ct), ct);

        logger.LogInformation("Pedido {PedidoId}: {Validos} convites válidos para a festa.", pedido.Id, validos);

        return validos;
    }

    /// <summary>
    /// Emite os convites 1 a N de uma compra paga na loja — de novo não cria nada (Sprint 26, decisão 9).
    /// </summary>
    /// <param name="compraId">A compra.</param>
    /// <param name="quantidade">Quantos convites ela pagou.</param>
    /// <returns>Quantos convites da compra valem; 409 se a festa não está completa na agenda.</returns>
    public async Task<Result<int>> EmitirDaCompra(Guid compraId, int quantidade, CancellationToken ct = default)
    {
        var festa = await Festa(ct);
        if (festa.Falhou)
            return Result.Falha<int>(festa.Erros);

        var validos = await convites.EmitirDaCompra(festa.Valor.Id, compraId, quantidade, await Prefixo(ct), ct);

        logger.LogInformation("Compra {CompraId}: {Validos} convites válidos para a festa.", compraId, validos);

        return validos;
    }

    /// <summary>
    /// Emite a cota que falta nos eventos com cota aberta — a turma inteira, ou só quem acabou de entrar (P1).
    /// </summary>
    /// <remarks>
    /// Turma sem cota aberta não emite nada, e a instrução é a mesma: quem chama na entrada da turma não
    /// precisa saber se a colação já abriu.
    /// </remarks>
    /// <param name="formaturaId">Turma.</param>
    /// <param name="vinculoId">Só este vínculo; nulo é a turma inteira.</param>
    /// <returns>Quantos convites nasceram agora.</returns>
    public async Task<int> EmitirDaCota(Guid formaturaId, Guid? vinculoId, CancellationToken ct = default)
    {
        var emitidos = await convites.EmitirDaCota(formaturaId, vinculoId, await Prefixo(formaturaId, ct), ct);

        if (emitidos > 0)
            logger.LogInformation(
                "Formatura {FormaturaId}: {Emitidos} convites de cota emitidos (vínculo {VinculoId}).",
                formaturaId,
                emitidos,
                vinculoId
            );

        return emitidos;
    }

    /// <summary>Revoga os convites de cota de quem saiu da turma, sob a trava das linhas (decisão 5).</summary>
    /// <param name="vinculoId">Quem saiu.</param>
    /// <returns>Quantos foram revogados agora.</returns>
    public async Task<int> RevogarDaCota(Guid vinculoId, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var revogados = (await convites.TravarDaCota(vinculoId, ct)).Count(convite => convite.Revogar(MotivoDaSaida, agora));

        if (revogados > 0)
            logger.LogInformation("Vínculo {VinculoId}: {Revogados} convites de cota revogados.", vinculoId, revogados);

        return revogados;
    }

    /// <summary>
    /// Revoga os convites do pedido além dos que ele ainda paga, sob a trava das linhas (decisão 15).
    /// </summary>
    /// <param name="pedidoId">Pedido.</param>
    /// <param name="manter">Quantos convites continuam valendo — os de posição até este número.</param>
    /// <param name="motivo">O que a portaria vai mostrar.</param>
    /// <returns>Quantos foram revogados agora.</returns>
    public async Task<int> RevogarDoPedido(Guid pedidoId, int manter, string motivo, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        var revogados = (await convites.TravarDoPedido(pedidoId, ct)).Count(convite => convite.Sequencial > manter && convite.Revogar(motivo, agora));

        if (revogados > 0)
            logger.LogInformation("Pedido {PedidoId}: {Revogados} convites revogados ({Motivo}).", pedidoId, revogados, motivo);

        return revogados;
    }
}
