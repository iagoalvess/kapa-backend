using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Validators;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;
using Backend.Business.Festa.Services;
using Backend.Business.Formandos.Interfaces;
using Backend.Business.Loja.Models;
using Backend.Business.Pagamentos.Services;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Cobrancas.Services;

/// <summary>
/// O cancelamento de pacote e de pedido como solicitação à comissão (Sprint 48, D8/D9/D12/D41).
/// </summary>
/// <remarks>
/// Aprovar é cancelar <b>tudo</b>: o pedido cai inteiro (o mesmo <see cref="IPedidoService.Cancelar"/> da tesouraria,
/// com o pago como crédito), e o pacote sai da cesta com todas as parcelas — pagas inclusive —, que é o que leva o já
/// pago à lista "a devolver" (D9). Os convites do pacote que não entraram na portaria são revogados (D41).
/// </remarks>
/// <param name="solicitacoes">Solicitações.</param>
/// <param name="pedidos">A trava do item e o pedido.</param>
/// <param name="planos">A cesta e o catálogo.</param>
/// <param name="parcelas">As parcelas do pacote.</param>
/// <param name="perfis">Vínculo de quem pede.</param>
/// <param name="pedidoService">O cancelamento do pedido, o mesmo da tesouraria.</param>
/// <param name="abertura">A abertura da solicitação, comum ao pedido.</param>
/// <param name="emissao">Os convites do pacote.</param>
/// <param name="valoresADevolver">A lista "a devolver".</param>
/// <param name="validator">Forma da solicitação.</param>
/// <param name="eventos">Trilha de auditoria.</param>
/// <param name="unitOfWork">Fronteira transacional.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class SolicitacaoDeCancelamentoService(
    ISolicitacaoDeCancelamentoRepository solicitacoes,
    IPedidoRepository pedidos,
    IPlanoDeCobrancaRepository planos,
    IParcelaRepository parcelas,
    IPerfilRepository perfis,
    IPedidoService pedidoService,
    AberturaDeSolicitacao abertura,
    EmissaoDeConvites emissao,
    ValoresADevolver valoresADevolver,
    IValidator<DadosDaSolicitacao> validator,
    IEventoRepository eventos,
    IUnitOfWork unitOfWork,
    ILogger<SolicitacaoDeCancelamentoService> logger
) : ISolicitacaoDeCancelamentoService
{
    private static readonly Erro NaoEncontrada = Erro.NaoEncontrado(
        "cobranca.solicitacao_nao_encontrada",
        "Solicitação de cancelamento não encontrada."
    );

    private static readonly Erro NadaACancelar = Erro.NaoEncontrado(
        "cobranca.nada_a_cancelar",
        "Este item não está na sua cesta nem nos seus pedidos."
    );

    private static readonly Erro JaRespondida = Erro.Conflito("cobranca.solicitacao_ja_respondida", "Esta solicitação já foi respondida.");

    private static readonly Erro SemVinculo = Erro.NaoEncontrado("formatura.vinculo_nao_encontrado", "Você não é membro ativo desta turma.");

    /// <inheritdoc />
    /// <remarks>
    /// Só o que é do formando se cancela: o pedido confirmado dele ou um pacote da cesta. O rateio e o lançamento
    /// avulso não — são da turma ou da tesouraria, e quem os desfaz é ela.
    /// </remarks>
    public async Task<Result<ResumoDaSolicitacao>> Solicitar(
        Guid formaturaId,
        Guid usuarioId,
        DadosDaSolicitacao dados,
        CancellationToken ct = default
    )
    {
        var validacao = validator.Validar(dados);
        if (validacao.Falhou)
            return Result.Falha<ResumoDaSolicitacao>(validacao.Erros);

        if (await perfis.ObterMembro(formaturaId, usuarioId, ct) is not { } membro)
            return SemVinculo;

        var aberta = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var item = await pedidos.TravarItem(dados.ItemDeCobrancaId, token);
                if (item is null)
                    return Result.Falha(NadaACancelar);

                Guid? pedidoId = null;

                if (item.Opcional)
                {
                    if (await pedidos.ObterParaEdicao(membro.VinculoId, item.Id, token) is not { Confirmado: true } pedido)
                        return Result.Falha(NadaACancelar);

                    pedidoId = pedido.Id;
                }
                else if (!item.Pacote || await planos.ObterEscolhaParaEdicao(membro.VinculoId, item.Id, token) is null)
                {
                    return Result.Falha(NadaACancelar);
                }

                var aberta = await abertura.Abrir(membro.VinculoId, item, pedidoId, dados.Motivo, usuarioId, token);
                if (aberta.Falhou)
                    return aberta;

                await unitOfWork.SalvarAsync(token);

                return Result.Ok();
            },
            ct
        );
        if (aberta.Falhou)
            return Result.Falha<ResumoDaSolicitacao>(aberta.Erros);

        var minhas = await solicitacoes.Listar(membro.VinculoId, StatusDoPedidoDeCancelamento.Aberto, ct);

        return minhas.First(solicitacao => solicitacao.ItemDeCobrancaId == dados.ItemDeCobrancaId);
    }

    /// <inheritdoc />
    /// <remarks>Do titular: quem foi desligado ainda lê a resposta do que pediu.</remarks>
    public async Task<Result<IReadOnlyList<ResumoDaSolicitacao>>> ListarMinhas(Guid formaturaId, Guid usuarioId, CancellationToken ct = default) =>
        await perfis.ObterTitular(formaturaId, usuarioId, ct) is { } membro
            ? Result.Ok(await solicitacoes.Listar(membro.VinculoId, null, ct))
            : Result.Falha<IReadOnlyList<ResumoDaSolicitacao>>(SemVinculo);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ResumoDaSolicitacao>>> Listar(StatusDoPedidoDeCancelamento? status, CancellationToken ct = default) =>
        Result.Ok(await solicitacoes.Listar(null, status, ct));

    /// <inheritdoc />
    /// <remarks>
    /// Sob a trava da solicitação: o segundo clique em "Aprovar" espera o primeiro e encontra a solicitação respondida.
    /// </remarks>
    public async Task<Result<ResumoDaSolicitacao>> Aprovar(Guid formaturaId, Guid solicitacaoId, Guid usuarioId, CancellationToken ct = default)
    {
        var aprovada = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var solicitacao = await solicitacoes.Travar(solicitacaoId, token);
                if (solicitacao is null)
                    return Result.Falha(NaoEncontrada);

                if (solicitacao.Status != StatusDoPedidoDeCancelamento.Aberto)
                    return Result.Falha(JaRespondida);

                var aDevolver = 0L;

                if (solicitacao.PedidoId is { } pedidoId)
                {
                    var cancelado = await pedidoService.Cancelar(
                        pedidoId,
                        formaturaId,
                        usuarioId,
                        new CancelamentoDePedido(TudoADevolver: true),
                        token
                    );
                    if (cancelado.Falhou)
                        return Result.Falha(cancelado.Erros);

                    aDevolver = cancelado.Valor!.PagoEmCentavos;
                }
                else
                {
                    aDevolver = await CancelarPacote(solicitacao, token);
                }

                await Retomar(solicitacao, token);
                solicitacao.Responder(aprovada: true, usuarioId, null, DateTime.UtcNow);

                await eventos.Auditar(
                    NomesDeAuditoria.CancelamentoAprovado,
                    usuarioId,
                    new
                    {
                        formaturaId = solicitacao.FormaturaId,
                        solicitacaoId,
                        itemId = solicitacao.ItemDeCobrancaId,
                        solicitacao.PedidoId,
                        aDevolverEmCentavos = aDevolver,
                    },
                    token
                );

                await unitOfWork.SalvarAsync(token);

                logger.LogInformation("Solicitação {SolicitacaoId} aprovada; {ADevolver} centavos a devolver.", solicitacaoId, aDevolver);

                return Result.Ok();
            },
            ct
        );

        return aprovada.Falhou ? Result.Falha<ResumoDaSolicitacao>(aprovada.Erros) : await Resumir(solicitacaoId, ct);
    }

    /// <inheritdoc />
    public async Task<Result<ResumoDaSolicitacao>> Recusar(Guid solicitacaoId, string motivo, Guid usuarioId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length > DadosDaSolicitacaoValidator.TamanhoDoTexto)
            return Erro.Validacao(
                "cobranca.motivo_da_recusa",
                $"Diga ao formando por que a comissão recusou, em até {DadosDaSolicitacaoValidator.TamanhoDoTexto} caracteres.",
                campo: "motivo"
            );

        var recusada = await unitOfWork.EmTransacaoAsync(
            async token =>
            {
                var solicitacao = await solicitacoes.Travar(solicitacaoId, token);
                if (solicitacao is null)
                    return Result.Falha(NaoEncontrada);

                if (!solicitacao.Responder(aprovada: false, usuarioId, motivo, DateTime.UtcNow))
                    return Result.Falha(JaRespondida);

                await Retomar(solicitacao, token);

                await eventos.Auditar(
                    NomesDeAuditoria.CancelamentoRecusado,
                    usuarioId,
                    new
                    {
                        formaturaId = solicitacao.FormaturaId,
                        solicitacaoId,
                        itemId = solicitacao.ItemDeCobrancaId,
                        solicitacao.PedidoId,
                        motivo = motivo.Trim(),
                    },
                    token
                );

                await unitOfWork.SalvarAsync(token);

                return Result.Ok();
            },
            ct
        );

        return recusada.Falhou ? Result.Falha<ResumoDaSolicitacao>(recusada.Erros) : await Resumir(solicitacaoId, ct);
    }

    /// <summary>
    /// Tira o pacote da cesta: todas as parcelas dele deixam de valer, o pago vai para "a devolver" e os convites
    /// além do que a cesta ainda concede são revogados.
    /// </summary>
    /// <remarks>
    /// A escolha sai antes da conta dos convites: o que a cesta "ainda concede" é lido do banco depois de salvar.
    /// </remarks>
    /// <param name="solicitacao">Solicitação travada, de pacote.</param>
    /// <returns>Quanto foi para "a devolver", em centavos.</returns>
    private async Task<long> CancelarPacote(SolicitacaoDeCancelamento solicitacao, CancellationToken ct)
    {
        await pedidos.TravarItem(solicitacao.ItemDeCobrancaId, ct);

        var desfeitas = (await parcelas.ListarDoVinculoNoItemParaEdicao(solicitacao.VinculoId, solicitacao.ItemDeCobrancaId, ct))
            .Where(parcela => parcela.Desfazer())
            .ToList();
        var aDevolver = await valoresADevolver.RegistrarParciais(desfeitas, ct);

        if (await planos.ObterEscolhaParaEdicao(solicitacao.VinculoId, solicitacao.ItemDeCobrancaId, ct) is { } escolha)
            planos.RemoverEscolha(escolha);

        await unitOfWork.SalvarAsync(ct);

        var restantes = (await planos.ListarCesta(solicitacao.VinculoId, ct)).ToHashSet();
        var cesta = (await planos.ObterVigente(ct))?.Itens.Where(item => restantes.Contains(item.Id)).ToList() ?? [];

        await emissao.RevogarAlemDaCesta(
            solicitacao.VinculoId,
            cesta.Sum(item => item.ConvitesDaFesta),
            cesta.Sum(item => item.ConvitesDaColacao),
            ct
        );

        return aDevolver;
    }

    /// <summary>A cobrança das parcelas do item volta no mesmo dia (D12): a comissão respondeu.</summary>
    /// <param name="solicitacao">Solicitação respondida.</param>
    private async Task Retomar(SolicitacaoDeCancelamento solicitacao, CancellationToken ct)
    {
        foreach (var parcela in await parcelas.ListarDoVinculoNoItemParaEdicao(solicitacao.VinculoId, solicitacao.ItemDeCobrancaId, ct))
            parcela.Retomar();
    }

    /// <summary>A solicitação como as telas a mostram, relida depois da escrita.</summary>
    /// <param name="solicitacaoId">Solicitação.</param>
    private async Task<Result<ResumoDaSolicitacao>> Resumir(Guid solicitacaoId, CancellationToken ct) =>
        await solicitacoes.Obter(solicitacaoId, ct) is { } resumo ? resumo : Result.Falha<ResumoDaSolicitacao>(NaoEncontrada);
}
