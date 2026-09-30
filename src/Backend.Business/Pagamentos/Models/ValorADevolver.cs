using Backend.Business.Abstractions;
using Backend.Business.Cobrancas.Models;

namespace Backend.Business.Pagamentos.Models;

/// <summary>De onde veio o dinheiro que a comissão tem de resolver. Gravado como texto.</summary>
public enum OrigemDoValorADevolver
{
    /// <summary>A tesouraria cancelou um pedido já pago e lançou o crédito (Sprint 42, decisão 2).</summary>
    CreditoDePedido,

    /// <summary>
    /// Uma parcela com pagamento parcial foi cancelada — desligamento, item encerrado, pedido cancelado ou cancelamento
    /// avulso (decisão 3): o que já tinha entrado não paga mais nada.
    /// </summary>
    ParcelaCancelada,

    /// <summary>
    /// O Mercado Pago confirmou um pagamento e não havia parcela aberta para baixar (decisão 9): o dinheiro está na
    /// conta dele, fora do caixa.
    /// </summary>
    PagoSemParcela,
}

/// <summary>Em que ponto está um valor a devolver. Gravado como texto.</summary>
public enum StatusDoValorADevolver
{
    /// <summary>Esperando a comissão.</summary>
    ADevolver,

    /// <summary>A comissão fez o PIX de volta e anexou o comprovante: a saída entrou no caixa como despesa paga.</summary>
    Devolvido,

    /// <summary>
    /// Fechado sem saída no caixa: o pago sem parcela que a comissão resolveu, ou o valor que o Mercado Pago devolveu
    /// sozinho — a devolução no painel ou a contestação no cartão.
    /// </summary>
    Fechado,
}

/// <summary>
/// Dinheiro de um formando que entrou e não paga mais nada — até a comissão resolver (Sprint 42, decisões 2, 3 e 9).
/// </summary>
/// <remarks>
/// O Kapa registra, a comissão resolve: nada aqui move dinheiro. O crédito deixou de ser parcela negativa, que ficava
/// aberta para sempre e travava o encerramento da turma (F1); o parcial da parcela cancelada deixou de sumir (F2); e o
/// pago sem parcela deixou de ser só um log (F7). Enquanto está <see cref="StatusDoValorADevolver.ADevolver"/>, conta
/// como pendência para encerrar a turma.
/// <para>
/// O recebimento que trouxe o dinheiro fica como está: a entrada aconteceu. A volta é uma saída nova, a despesa paga
/// do dia da devolução — o mês fechado não muda (decisão 6).
/// </para>
/// </remarks>
public class ValorADevolver : EntidadeDaFormatura
{
    /// <summary>De quem é o dinheiro.</summary>
    public Guid VinculoId { get; private set; }

    /// <summary>De onde veio.</summary>
    public OrigemDoValorADevolver Origem { get; private set; }

    /// <summary>O item da parcela ou do pedido — é o que a lista mostra como "do quê".</summary>
    public Guid ItemDeCobrancaId { get; private set; }

    /// <summary>
    /// Quanto falta devolver, em centavos. Diminui quando o Mercado Pago devolve parte sozinho (<see cref="Abater"/>).
    /// </summary>
    public long ValorEmCentavos { get; private set; }

    /// <summary>A parcela cancelada com pagamento, ou a primeira do pago sem parcela.</summary>
    public Guid? ParcelaId { get; private set; }

    /// <summary>O pedido cancelado com crédito.</summary>
    public Guid? PedidoId { get; private set; }

    /// <summary>A cobrança do Mercado Pago que pagou sem parcela.</summary>
    public Guid? CobrancaId { get; private set; }

    /// <summary>Situação.</summary>
    public StatusDoValorADevolver Status { get; private set; } = StatusDoValorADevolver.ADevolver;

    /// <summary>Quando saiu da lista, em UTC.</summary>
    public DateTime? ResolvidoEm { get; private set; }

    /// <summary>Quem tirou da lista; nulo quando foi o Mercado Pago.</summary>
    public Guid? ResolvidoPorUsuarioId { get; private set; }

    /// <summary>O comprovante do PIX de volta.</summary>
    public Guid? ComprovanteArquivoId { get; private set; }

    /// <summary>A despesa paga que a devolução virou no caixa.</summary>
    public Guid? DespesaId { get; private set; }

    /// <summary>O que a comissão fez com o pago sem parcela, ou o motivo do fechamento automático.</summary>
    public string? Observacao { get; private set; }

    /// <summary>Construtor do EF.</summary>
    protected ValorADevolver() { }

    private ValorADevolver(Guid vinculoId, Guid itemId, OrigemDoValorADevolver origem, long valorEmCentavos)
    {
        if (valorEmCentavos <= 0)
            throw new ArgumentOutOfRangeException(nameof(valorEmCentavos), valorEmCentavos, "O valor a devolver é positivo.");

        VinculoId = vinculoId;
        ItemDeCobrancaId = itemId;
        Origem = origem;
        ValorEmCentavos = valorEmCentavos;
    }

    /// <summary>O crédito do pedido cancelado.</summary>
    /// <param name="pedido">Pedido.</param>
    /// <param name="valorEmCentavos">Crédito, já limitado ao que o formando pagou nele.</param>
    public static ValorADevolver DoCredito(Pedido pedido, long valorEmCentavos) =>
        new(pedido.VinculoId, pedido.ItemDeCobrancaId, OrigemDoValorADevolver.CreditoDePedido, valorEmCentavos) { PedidoId = pedido.Id };

    /// <summary>O que já tinha entrado na parcela que acabou de ser cancelada.</summary>
    /// <param name="parcela">Parcela cancelada, com o pago dela.</param>
    public static ValorADevolver DaParcela(Parcela parcela) =>
        new(parcela.VinculoId, parcela.ItemDeCobrancaId, OrigemDoValorADevolver.ParcelaCancelada, parcela.ValorPagoEmCentavos ?? 0)
        {
            ParcelaId = parcela.Id,
        };

    /// <summary>O pagamento do Mercado Pago que não achou parcela aberta.</summary>
    /// <param name="parcela">A parcela que ele ia baixar, para a tela dizer do que era.</param>
    /// <param name="cobrancaId">Cobrança.</param>
    /// <param name="valorEmCentavos">O que não baixou nada.</param>
    public static ValorADevolver DoPagoSemParcela(Parcela parcela, Guid cobrancaId, long valorEmCentavos) =>
        new(parcela.VinculoId, parcela.ItemDeCobrancaId, OrigemDoValorADevolver.PagoSemParcela, valorEmCentavos)
        {
            CobrancaId = cobrancaId,
            ParcelaId = parcela.Id,
        };

    /// <summary>
    /// A comissão devolveu: sai da lista com o comprovante e a despesa que a saída virou.
    /// </summary>
    /// <remarks>O pago sem parcela não passa por aqui: o dinheiro nunca entrou no caixa, então não há saída a lançar.</remarks>
    /// <param name="usuarioId">Quem registrou.</param>
    /// <param name="comprovanteArquivoId">Comprovante do PIX.</param>
    /// <param name="despesaId">A saída no caixa.</param>
    /// <param name="agoraUtc">Agora.</param>
    public Result Devolver(Guid usuarioId, Guid comprovanteArquivoId, Guid despesaId, DateTime agoraUtc)
    {
        if (Status != StatusDoValorADevolver.ADevolver || Origem == OrigemDoValorADevolver.PagoSemParcela)
            return Result.Falha(NaoADevolver);

        Status = StatusDoValorADevolver.Devolvido;
        ResolvidoEm = agoraUtc;
        ResolvidoPorUsuarioId = usuarioId;
        ComprovanteArquivoId = comprovanteArquivoId;
        DespesaId = despesaId;

        return Result.Ok();
    }

    /// <summary>
    /// Fecha sem saída no caixa — a comissão resolveu o pago sem parcela (devolveu no painel ou lançou como outra
    /// receita), ou o Mercado Pago devolveu sozinho.
    /// </summary>
    /// <param name="usuarioId">Quem fechou; nulo quando foi o Mercado Pago.</param>
    /// <param name="observacao">O que foi feito.</param>
    /// <param name="agoraUtc">Agora.</param>
    public Result Fechar(Guid? usuarioId, string observacao, DateTime agoraUtc)
    {
        if (Status != StatusDoValorADevolver.ADevolver)
            return Result.Falha(NaoADevolver);

        if (usuarioId is not null && Origem != OrigemDoValorADevolver.PagoSemParcela)
            return Result.Falha(
                Erro.Conflito(
                    "pagamento.devolucao_exige_comprovante",
                    "Este valor voltou ao formando por PIX: registre a devolução com o comprovante."
                )
            );

        Status = StatusDoValorADevolver.Fechado;
        ResolvidoEm = agoraUtc;
        ResolvidoPorUsuarioId = usuarioId;
        Observacao = observacao.Trim();

        return Result.Ok();
    }

    /// <summary>
    /// O Mercado Pago devolveu parte do que entrou na parcela cancelada: o que falta devolver diminui, e zerado, fecha.
    /// </summary>
    /// <param name="valorEmCentavos">O estorno que o Mercado Pago fez.</param>
    /// <param name="motivo">Devolução no painel ou contestação.</param>
    /// <param name="agoraUtc">Agora.</param>
    public void Abater(long valorEmCentavos, string motivo, DateTime agoraUtc)
    {
        if (Status != StatusDoValorADevolver.ADevolver)
            return;

        ValorEmCentavos = Math.Max(0, ValorEmCentavos - valorEmCentavos);

        if (ValorEmCentavos == 0)
            Fechar(null, motivo, agoraUtc);
    }

    /// <summary>O valor já saiu da lista — devolvido, fechado, ou é de outro tipo.</summary>
    public static readonly Erro NaoADevolver = Erro.Conflito("pagamento.valor_nao_a_devolver", "Este valor não está mais na lista a devolver.");
}
