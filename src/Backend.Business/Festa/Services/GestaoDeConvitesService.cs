using Backend.Business.Abstractions;
using Backend.Business.Agenda.Interfaces;
using Backend.Business.Agenda.Models;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Festa.Interfaces;
using Backend.Business.Festa.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Loja.Interfaces;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Festa.Services;

/// <summary>
/// O convite da festa do lado da Gestão: reemitir, liberar, a cortesia, emitir os pendentes e o resumo.
/// </summary>
/// <remarks>
/// A emissão comum não passa por aqui: ela nasce da quitação do pedido, na transação da baixa (P2). O que está
/// aqui são as exceções — a liberação manual e a cortesia —, que exigem motivo e ficam na auditoria com o autor.
/// Escrita de convite existente trava a linha antes (decisão 15), como no <see cref="ConviteDoEventoService"/>.
/// <para>Log só com ids: nome e documento de convidado são dado de terceiro.</para>
/// </remarks>
/// <param name="convites">Convites da turma.</param>
/// <param name="pedidos">Pedidos de convite extra e o item que a cortesia ocupa.</param>
/// <param name="compras">As compras da loja pagas enquanto a festa estava incompleta (Sprint 26).</param>
/// <param name="agenda">A festa, para o resumo.</param>
/// <param name="emissao">Emissão idempotente e prefixo dos códigos.</param>
/// <param name="codigos">Sorteio e assinatura.</param>
/// <param name="emails">O convite para o convidado da cortesia.</param>
/// <param name="eventos">Auditoria.</param>
/// <param name="cortesiaValidator">Forma da cortesia.</param>
/// <param name="liberacaoValidator">Forma da liberação.</param>
/// <param name="formaturaAtual">Turma da sessão, para a auditoria da cortesia.</param>
/// <param name="formaturas">Nome e instituição da turma, para o PDF que vai anexo ao e-mail do convidado.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class GestaoDeConvitesService(
    IConviteDoEventoRepository convites,
    IPedidoRepository pedidos,
    ICompraDeConviteRepository compras,
    IEventoDaTurmaRepository agenda,
    EmissaoDeConvites emissao,
    CodigoDoConvite codigos,
    EmailsDoConvite emails,
    IEventoRepository eventos,
    IValidator<DadosDaCortesia> cortesiaValidator,
    IValidator<LiberacaoDeConvites> liberacaoValidator,
    IFormaturaAtual formaturaAtual,
    IFormaturaRepository formaturas,
    IUnitOfWork unitOfWork,
    ILogger<GestaoDeConvitesService> logger
) : IGestaoDeConvitesService
{
    private static readonly Erro PedidoNaoEncontrado = Erro.NaoEncontrado("cobranca.pedido_nao_encontrado", "Pedido não encontrado.");

    /// <inheritdoc />
    public async Task<Result<ConviteNaPortaria>> Reemitir(Guid conviteId, Guid usuarioId, CancellationToken ct = default)
    {
        var prefixo = await emissao.Prefixo(ct);

        var reemitido = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var convite = await convites.Travar(conviteId, token);
                if (convite is not { Valido: true })
                    return Result.Falha<string>(ErrosDoConvite.NaoEncontrado);

                convite.Revogar("reemitido — o código anterior não vale mais", DateTime.UtcNow);
                await unitOfWork.SalvarAsync(token);

                var novo = convite.Substituto(CodigoDoConvite.Sortear(prefixo), null);
                await convites.Adicionar(novo, token);

                await eventos.Auditar(
                    NomesDeAuditoria.ConviteReemitido,
                    usuarioId,
                    new
                    {
                        formaturaId = convite.FormaturaId,
                        conviteId = convite.Id,
                        novoConviteId = novo.Id,
                    },
                    token
                );

                await unitOfWork.SalvarAsync(token);

                return Result.Ok(novo.Codigo);
            },
            ct
        );

        if (reemitido.Falhou)
            return Result.Falha<ConviteNaPortaria>(reemitido.Erros);

        return await NaPortaria(reemitido.Valor, ct);
    }

    /// <inheritdoc />
    public async Task<Result<int>> Liberar(Guid usuarioId, LiberacaoDeConvites dados, CancellationToken ct = default)
    {
        var validacao = liberacaoValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<int>(validacao.Erros);

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var pedido = await pedidos.ObterParaEdicao(dados.PedidoId, token);
                if (pedido is not { Confirmado: true })
                    return Result.Falha<int>(PedidoNaoEncontrado);

                if (await pedidos.Obter(pedido.Id, token) is not { Tipo: TipoDeCobranca.ConviteExtra })
                    return Result.Falha<int>(Erro.Conflito("festa.pedido_sem_convite", "Este pedido não é de convite extra."));

                var emitidos = await emissao.EmitirDoPedido(pedido, token);
                if (emitidos.Falhou)
                    return emitidos;

                await eventos.Auditar(
                    NomesDeAuditoria.ConvitesLiberados,
                    usuarioId,
                    new
                    {
                        formaturaId = pedido.FormaturaId,
                        pedidoId = pedido.Id,
                        convites = emitidos.Valor,
                        motivo = dados.Motivo.Trim(),
                    },
                    token
                );

                await unitOfWork.SalvarAsync(token);

                return emitidos;
            },
            ct
        );
    }

    /// <inheritdoc />
    public async Task<Result<ConviteNaPortaria>> EmitirCortesia(Guid usuarioId, DadosDaCortesia dados, CancellationToken ct = default)
    {
        var validacao = cortesiaValidator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ConviteNaPortaria>(validacao.Erros);

        var doEvento = await emissao.Evento(dados.EventoId, ct);
        if (doEvento.Falhou)
            return Result.Falha<ConviteNaPortaria>(doEvento.Erros);

        var convidado = ConviteDoEventoService.Normalizar(dados.Convidado);
        var prefixo = await emissao.Prefixo(ct);
        var itemId = doEvento.Valor.Tipo is TipoDeEvento.Festa ? await pedidos.ObterItemDeConviteEmVenda(ct) : null;

        var emitida = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                if (itemId is { } id && await pedidos.TravarItem(id, token) is { } item)
                {
                    var ocupar = item.Ocupar(1);
                    if (ocupar.Falhou)
                        return Result.Falha<ConviteDoEvento>(ocupar.Erros);
                }

                var convite = ConviteDoEvento.Cortesia(doEvento.Valor.Id, CodigoDoConvite.Sortear(prefixo), convidado);
                await convites.Adicionar(convite, token);

                if (convite.EmailDoConvidado is { } email)
                    await emails.Enviado(
                        email,
                        await ConviteDoEventoService.ParaPublico(convite, doEvento.Valor, formaturaAtual, formaturas, codigos, token),
                        token
                    );

                await eventos.Auditar(
                    NomesDeAuditoria.CortesiaEmitida,
                    usuarioId,
                    new
                    {
                        formaturaId = formaturaAtual.Id,
                        conviteId = convite.Id,
                        eventoId = doEvento.Valor.Id,
                        itemDeCobrancaId = itemId,
                        motivo = dados.Motivo.Trim(),
                    },
                    token
                );

                await unitOfWork.SalvarAsync(token);

                return Result.Ok(convite);
            },
            ct
        );

        if (emitida.Falhou)
            return Result.Falha<ConviteNaPortaria>(emitida.Erros);

        logger.LogInformation("Cortesia {ConviteId} emitida por {UsuarioId}.", emitida.Valor.Id, usuarioId);

        return await NaPortaria(emitida.Valor.Codigo, ct);
    }

    /// <inheritdoc />
    public async Task<Result<int>> EmitirPendentes(CancellationToken ct = default)
    {
        var festa = await emissao.Festa(ct);
        if (festa.Falhou)
            return Result.Falha<int>(festa.Erros);

        var pendentes = (await pedidos.ListarDeConviteQuitados(festa.Valor.Id, ct))
            .Where(linha => linha.ConvitesValidos < linha.Pedido.Quantidade)
            .ToList();
        var comprasPendentes = await compras.ListarPagasSemConvite(festa.Valor.Id, ct);

        return await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                foreach (var linha in pendentes)
                {
                    var emitidos = await emissao.EmitirDoPedido(linha.Pedido, token);
                    if (emitidos.Falhou)
                        return emitidos;
                }

                foreach (var compra in comprasPendentes)
                {
                    var emitidos = await emissao.EmitirDaCompra(compra, token);
                    if (emitidos.Falhou)
                        return emitidos;
                }

                return Result.Ok(pendentes.Count + comprasPendentes.Count);
            },
            ct
        );
    }

    /// <inheritdoc />
    public async Task<Result<ResumoDosConvites>> Resumir(CancellationToken ct = default)
    {
        if (await agenda.ObterDoTipo(TipoDeEvento.Festa, ct) is not { } festa)
            return new ResumoDosConvites(null, false, 0, 0, 0, 0);

        var evento = EmissaoDeConvites.ParaConvite(festa);
        var (emitidos, semTitular) = await convites.Contar(festa.Id, ct);
        var quitadosSemConvite =
            (await pedidos.ListarDeConviteQuitados(festa.Id, ct)).Count(linha => linha.ConvitesValidos < linha.Pedido.Quantidade)
            + (await compras.ListarPagasSemConvite(festa.Id, ct)).Count;
        var diaDoFechamento = DateOnly.FromDateTime(DataUtils.ParaExibicao(evento.FechamentoEmUtc));
        var tarde = await pedidos.ContarDeConviteComParcelaDepoisDe(diaDoFechamento, ct);

        return new ResumoDosConvites(evento, evento.Completo, emitidos, semTitular, quitadosSemConvite, tarde);
    }

    /// <summary>O convite recém-escrito como a portaria o mostra.</summary>
    private async Task<Result<ConviteNaPortaria>> NaPortaria(string codigo, CancellationToken ct) =>
        await convites.ObterNaPortaria(codigo, ct) is { } gravado ? Portaria.ParaPortaria(gravado) : ErrosDoConvite.NaoEncontrado;
}
