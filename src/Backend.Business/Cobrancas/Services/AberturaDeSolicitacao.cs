using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Interfaces;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Cobrancas.Validators;
using Backend.Business.Common.Datas;
using Backend.Business.Eventos.Interfaces;
using Backend.Business.Eventos.Models;
using Backend.Business.Eventos.Services;

namespace Backend.Business.Cobrancas.Services;

/// <summary>
/// Abrir a solicitação de cancelamento do formando — a porta única do cancelamento dele (Sprint 48, D8).
/// </summary>
/// <remarks>
/// Os dois caminhos passam por aqui: o pacote da cesta (<see cref="SolicitacaoDeCancelamentoService"/>) e o
/// <c>POST /pedidos/{id}/cancelar</c> do formando (<see cref="PedidoService"/>). Sem interface, como
/// <c>ValoresADevolver</c>, e sem <c>SalvarAsync</c>: a solicitação entra na transação de quem chama, que já travou a
/// linha do item — é essa trava que faz do clique duplo uma solicitação só, e o índice único parcial é a garantia.
/// </remarks>
/// <param name="solicitacoes">Solicitações.</param>
/// <param name="parcelas">As parcelas a suspender.</param>
/// <param name="eventos">Trilha de auditoria.</param>
public sealed class AberturaDeSolicitacao(ISolicitacaoDeCancelamentoRepository solicitacoes, IParcelaRepository parcelas, IEventoRepository eventos)
{
    /// <summary>
    /// Abre a solicitação e suspende as parcelas em aberto do item até o fim do prazo de resposta (D12/D37).
    /// </summary>
    /// <remarks>Pedir de novo com uma aberta não abre outra nem estende o prazo: devolve sucesso, sem efeito.</remarks>
    /// <param name="vinculoId">Quem pede.</param>
    /// <param name="item">Pacote ou item do pedido, travado por quem chama.</param>
    /// <param name="pedidoId">O pedido, se for avulso.</param>
    /// <param name="motivo">Por que, se disse.</param>
    /// <param name="usuarioId">Quem pede, para a trilha.</param>
    public async Task<Result> Abrir(Guid vinculoId, ItemDeCobranca item, Guid? pedidoId, string? motivo, Guid usuarioId, CancellationToken ct)
    {
        if (motivo?.Trim().Length > DadosDaSolicitacaoValidator.TamanhoDoTexto)
            return Result.Falha(
                Erro.Validacao(
                    "cobranca.motivo_longo",
                    $"Escreva o motivo em até {DadosDaSolicitacaoValidator.TamanhoDoTexto} caracteres.",
                    campo: "motivo"
                )
            );

        var hoje = DataUtils.Hoje();

        if (!item.CancelavelEm(hoje))
            return Result.Falha(
                Erro.Conflito(
                    "cobranca.cancelamento_fora_do_prazo",
                    $"O prazo para pedir o cancelamento terminou em {item.CancelavelAte:dd/MM/yyyy}. Fale com a comissão."
                )
            );

        if (await solicitacoes.ExisteAberta(vinculoId, item.Id, ct))
            return Result.Ok();

        var solicitacao = new SolicitacaoDeCancelamento(vinculoId, item.Id, pedidoId, motivo, DateTime.UtcNow, hoje);
        await solicitacoes.Adicionar(solicitacao, ct);

        foreach (var parcela in await parcelas.ListarDoVinculoNoItemParaEdicao(vinculoId, item.Id, ct))
            parcela.Suspender(solicitacao.RespostaAte);

        await eventos.Auditar(
            NomesDeAuditoria.CancelamentoSolicitado,
            usuarioId,
            new
            {
                formaturaId = item.FormaturaId,
                solicitacaoId = solicitacao.Id,
                itemId = item.Id,
                item.Descricao,
                item.Tipo,
                pedidoId,
                respostaAte = solicitacao.RespostaAte,
            },
            ct
        );

        return Result.Ok();
    }
}
