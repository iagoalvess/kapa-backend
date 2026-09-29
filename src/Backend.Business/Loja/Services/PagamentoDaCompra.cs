using Backend.Business.Abstractions;
using Backend.Business.Common.Datas;
using Backend.Business.Festa.Services;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Loja.Interfaces;
using Backend.Business.Loja.Models;
using Backend.Business.MercadoPago.Models;
using Microsoft.Extensions.Logging;

namespace Backend.Business.Loja.Services;

/// <summary>
/// O que acontece com a compra depois de criada: o pagamento que confirma e a reserva que vence (Sprint 26,
/// decisões 3, 4, 7 e 9).
/// </summary>
/// <remarks>
/// A confirmação não é um caminho novo: quem consulta o Mercado Pago e trava a cobrança é a
/// <c>BaixaAutomatica</c> da Sprint 25, chamada pelo aviso e pela conciliação; ela chama
/// <see cref="Confirmar"/> dentro da transação dela, em vez de baixar parcelas. Por isso aqui não há
/// <c>SalvarAsync</c> na confirmação — quem grava é ela.
/// <para>
/// Sem interface, como <c>BaixaService</c>: uma implementação, e ninguém de fora a substitui.
/// </para>
/// </remarks>
/// <param name="compras">Compras e a reserva no item.</param>
/// <param name="receitas">A outra receita que a compra paga vira (decisão 4).</param>
/// <param name="emissao">Os convites da compra (Sprint 21, decisão 12).</param>
/// <param name="emails">A confirmação e o "pagou sem lugar".</param>
/// <param name="cancelamento">O desfazer da compra, quando o Mercado Pago devolve o pagamento (Sprint 39).</param>
/// <param name="unitOfWork">Fronteira transacional da expiração.</param>
/// <param name="logger">Log estruturado.</param>
public sealed class PagamentoDaCompra(
    ICompraDeConviteRepository compras,
    IOutraReceitaRepository receitas,
    EmissaoDeConvites emissao,
    EmailsDaLoja emails,
    ICancelamentoDaCompraService cancelamento,
    IUnitOfWork unitOfWork,
    ILogger<PagamentoDaCompra> logger
)
{
    /// <summary>
    /// Registra o pagamento de uma compra, sob a trava dela, na transação de quem chama.
    /// </summary>
    /// <remarks>
    /// Pendente vira paga e ganha os convites. Expirada tenta reservar de novo: com lugar, vira paga; sem,
    /// vira "a devolver" e vai para a lista da comissão (decisão 9). As duas viram outra receita recebida —
    /// o dinheiro entrou na conta da turma de qualquer jeito, e é a turma quem devolve (P5).
    /// <para>
    /// Festa incompleta na agenda não derruba a confirmação: os convites saem depois, pelo mesmo botão que
    /// emite os pedidos quitados da Sprint 21.
    /// </para>
    /// </remarks>
    /// <param name="compraId">A compra.</param>
    /// <param name="pedido">O que o Mercado Pago respondeu — valor, data e CPF do pagador.</param>
    /// <param name="formaturaId">Turma, para o e-mail dizer quem vende.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Se confirmou agora; falso quando já estava resolvida.</returns>
    public async Task<bool> Confirmar(Guid compraId, PedidoConsultado pedido, Guid formaturaId, CancellationToken ct = default)
    {
        var compra = await compras.Travar(compraId, ct);
        if (compra is null)
            return false;

        var reservouDeNovo = compra.Status == StatusDaCompra.Expirada && await compras.ReservarNoItem(compra.ItemDeCobrancaId, compra.Quantidade, ct);
        var pagaEm = pedido.PagoEm ?? DateTime.UtcNow;

        if (!compra.Pagar(pedido.ValorPagoEmCentavos, pagaEm, pedido.CpfDoPagador, reservouDeNovo))
            return false;

        var item = await compras.ObterItem(compra.ItemDeCobrancaId, ct);
        var receita = OutraReceita.Nova(
            new NovaOutraReceita(
                $"Loja da turma — compra {compra.Id.ToString("N")[^8..].ToUpperInvariant()}: {compra.Quantidade}× {item?.Descricao ?? "Convite"}",
                null,
                CategoriaDeOutraReceita.VendaDeConvite,
                pedido.ValorPagoEmCentavos,
                DateOnly.FromDateTime(DataUtils.ParaExibicao(pagaEm)),
                Recebida: true
            )
        );

        await receitas.Adicionar(receita, ct);
        compra.Receita(receita.Id);

        var vendedor = await emails.Vendedor(formaturaId, ct);

        if (compra.Status == StatusDaCompra.Paga)
        {
            var emitidos = await emissao.EmitirDaCompra(compra, ct);
            if (emitidos.Falhou)
                logger.LogWarning("Compra {CompraId} paga sem convites: {Motivo}", compra.Id, emitidos.PrimeiroErro.Mensagem);

            if (compra.Email is not null)
                await emails.Confirmada(compra, vendedor, ct);
        }
        else
        {
            logger.LogWarning("Compra {CompraId} paga depois de expirar e sem lugar: vai para a lista de devolução.", compra.Id);

            if (compra.Email is not null)
                await emails.SemLugar(compra, vendedor, ct);
        }

        logger.LogInformation("Compra {CompraId} confirmada como {Status}.", compra.Id, compra.Status);

        return true;
    }

    /// <summary>
    /// O pagamento da compra voltou ao comprador pelo Mercado Pago (Sprint 39, P4 e P5): o caminho da Sprint 38 revoga
    /// os convites e estorna a receita. Na transação de quem chama — a da <c>BaixaAutomatica</c>, sob a trava da cobrança.
    /// </summary>
    /// <param name="compraId">A compra.</param>
    /// <param name="motivo">Por quê.</param>
    /// <param name="usuarioId">Em nome de quem.</param>
    /// <param name="ct">Token de cancelamento.</param>
    public Task<Result<string>> Devolver(Guid compraId, string motivo, Guid usuarioId, CancellationToken ct = default) =>
        cancelamento.DevolverPeloMercadoPago(compraId, motivo, usuarioId, ct);

    /// <summary>
    /// Expira a compra pendente vencida e devolve o estoque — condicional, então rodar de novo não devolve
    /// de novo (decisão 7).
    /// </summary>
    /// <remarks>
    /// Quem chama consulta o Mercado Pago <b>antes</b> (decisão 9): a compra paga cujo aviso se perdeu é
    /// confirmada pela conciliação, e aqui chega já paga — o <c>UPDATE</c> não a acha pendente.
    /// </remarks>
    /// <param name="compraId">A compra.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Se expirou agora.</returns>
    public async Task<Result<bool>> Expirar(Guid compraId, CancellationToken ct = default)
    {
        var expirou = await unitOfWork.EmTransacaoAsync(async token => Result.Ok(await compras.Expirar(compraId, DateTime.UtcNow, token)), ct);

        if (expirou is { Sucesso: true, Valor: true })
            logger.LogInformation("Compra {CompraId} expirou; o estoque voltou.", compraId);

        return expirou;
    }
}
