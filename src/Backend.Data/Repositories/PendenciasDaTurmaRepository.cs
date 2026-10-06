using Backend.Business.Assinaturas.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Loja.Models;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Models;
using Backend.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Backend.Data.Repositories;

/// <summary>
/// O que ainda está em aberto na formatura selecionada (Sprint 38, decisão 7).
/// </summary>
/// <remarks>
/// Cada pergunta é uma ida ao banco: as contagens vão como subconsultas escalares de um <c>SELECT</c> só, em
/// vez de uma consulta por tabela — é a porta de saída, e ela responde de uma vez tudo o que falta.
/// </remarks>
/// <param name="db">Contexto da requisição — o filtro global da formatura vale em todas as subconsultas.</param>
public sealed class PendenciasDaTurmaRepository(AppDbContext db) : IPendenciasDaTurmaRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// Compra de pé é a pendente, que segura o lugar, e a paga com lugar valendo — a cancelada inteira e a paga
    /// sem lugar não têm convite. Pedido de convite é o confirmado de item <c>ConviteExtra</c>, quitado ou não:
    /// os dois viram convite da festa.
    /// </remarks>
    public async Task<VendasDaFesta> ContarVendasDaFesta(CancellationToken ct = default) =>
        await db
            .Formaturas.AsNoTracking()
            .Where(f => f.Id == db.FormaturaAtualId)
            .Select(_ => new VendasDaFesta(
                db.ComprasDeConvite.Count(c =>
                    c.Status == StatusDaCompra.Pendente || (c.Status != StatusDaCompra.Expirada && c.ConvitesCancelados < c.Quantidade)
                ),
                (
                    from pedido in db.Pedidos
                    join item in db.ItensDeCobranca on pedido.ItemDeCobrancaId equals item.Id
                    where pedido.Status == StatusDoPedido.Confirmado && item.Tipo == TipoDeCobranca.ConviteExtra
                    select pedido.Id
                ).Count()
            ))
            .FirstAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Pedido não quitado é o confirmado com parcela aberta — a mesma regra de <c>PedidoRepository</c>. Cobrança
    /// viva é a que ainda pode ser paga: sendo emitida, ou emitida e dentro da validade.
    /// </remarks>
    public async Task<PendenciasDaTurma> ContarParaEncerrar(DateTime agora, CancellationToken ct = default) =>
        await db
            .Formaturas.AsNoTracking()
            .Where(f => f.Id == db.FormaturaAtualId)
            .Select(_ => new PendenciasDaTurma(
                db.Parcelas.Count(p => p.Status == StatusDaParcela.Aberta),
                db.Informes.Count(i => i.Status == StatusDoInforme.Pendente),
                db.Pedidos.Count(pedido =>
                    pedido.Status == StatusDoPedido.Confirmado
                    && db.Parcelas.Any(parcela =>
                        parcela.VinculoId == pedido.VinculoId
                        && parcela.ItemDeCobrancaId == pedido.ItemDeCobrancaId
                        && parcela.Status == StatusDaParcela.Aberta
                    )
                ),
                db.ComprasDeConvite.Count(c => c.Status == StatusDaCompra.Pendente),
                db.ComprasDeConvite.Count(c => c.Status == StatusDaCompra.ADevolver),
                db.PedidosDeCancelamento.Count(p => p.Status == StatusDoPedidoDeCancelamento.Aberto),
                db.CobrancasBancarias.Count(c =>
                    c.Status == StatusDaCobrancaBancaria.Emitindo || (c.Status == StatusDaCobrancaBancaria.Emitida && c.ExpiraEm > agora)
                ),
                db.ValoresADevolver.Count(v => v.Status == StatusDoValorADevolver.ADevolver)
            ))
            .FirstAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Uma ida ao banco, com um <c>EXISTS</c> ou um <c>COUNT</c> por passo — nada é carregado. O vínculo não é
    /// entidade da formatura, por isso leva a turma explícita; o resto já vem isolado pelo filtro global.
    /// <para>
    /// O recebimento segue a regra da tela até aqui: cobrança automática pelo Mercado Pago, ou um meio que dispensa
    /// conferência (transferência, dinheiro), ou o PIX com o titular conferido.
    /// </para>
    /// </remarks>
    public async Task<PrimeirosPassos> ConferirPrimeirosPassos(CancellationToken ct = default) =>
        await db
            .Formaturas.AsNoTracking()
            .Where(f => f.Id == db.FormaturaAtualId)
            .Select(f => new PrimeirosPassos(
                db.Vinculos.Count(v => v.FormaturaId == f.Id && v.Ativo && v.Papel != PapelNaFormatura.Formando) > 1,
                db.PlanosDeCobranca.Any(p => p.Status == StatusDoPlano.Vigente),
                db.TermosDeAdesao.Any(),
                db.CredenciaisDeProvedor.Any(c => c.CobrancaAutomaticaEm != null)
                    || db.ContasDeRecebimento.Any(c => c.Banco != null || c.DinheiroCom != null || (c.Chave != null && c.ConferidaEm != null)),
                db.Assinaturas.Any(a => a.Status != StatusDaAssinatura.Pendente),
                db.Vinculos.Any(v => v.FormaturaId == f.Id && v.Ativo && v.Papel == PapelNaFormatura.Formando)
            ))
            .FirstAsync(ct);
}
