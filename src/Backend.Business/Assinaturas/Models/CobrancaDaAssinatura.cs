using Backend.Business.Abstractions;
using Backend.Business.Pagamentos.Models;

namespace Backend.Business.Assinaturas.Models;

/// <summary>
/// Um pagamento do plano: o PIX de um ciclo, a diferença da subida de plano ou o débito do cartão recorrente
/// (Sprint 37). É o histórico da tela, a lista da nota fiscal e o que o suporte estorna.
/// </summary>
/// <remarks>
/// O PIX e a diferença nascem <see cref="SituacaoDaCobrancaDoPlano.Aberta"/>, com a página de pagamento do provedor
/// em <see cref="Url"/>, e o <see cref="Entity.Id"/> vai como referência externa — é por ele que o aviso do pagamento
/// volta. O débito do cartão nasce já paga, quando o aviso dele chega: quem o cria é o provedor, não o Kapa.
/// <para>
/// <b>Não</b> herda de <c>EntidadeDaFormatura</c>, como <see cref="EventoDeCobranca"/>: o débito do cartão nasce no
/// webhook e na conciliação, que atravessam turmas sem sessão. O isolamento vem da <see cref="Assinatura"/>: toda
/// leitura da comissão junta com ela, e ela passa pelo filtro da formatura.
/// </para>
/// </remarks>
public class CobrancaDaAssinatura : Entity
{
    /// <summary>Assinatura paga por esta cobrança.</summary>
    public Guid AssinaturaId { get; init; }

    /// <summary>O plano pago. Na diferença, o plano novo — é ele que passa a valer quando ela for paga.</summary>
    public Guid PlanoId { get; init; }

    /// <summary>Ciclo ou diferença de plano.</summary>
    public MotivoDaCobranca Motivo { get; init; }

    /// <summary>Meio pedido ao provedor.</summary>
    public MeioDePagamento Meio { get; init; }

    /// <summary>Valor em centavos: o pedido, enquanto aberta; o que o provedor creditou, depois de paga.</summary>
    public long ValorEmCentavos { get; private set; }

    /// <summary>Situação. Muda só pelos métodos daqui.</summary>
    public SituacaoDaCobrancaDoPlano Situacao { get; private set; } = SituacaoDaCobrancaDoPlano.Aberta;

    /// <summary>A página de pagamento do provedor, enquanto aberta.</summary>
    public string? Url { get; set; }

    /// <summary>O id do pagamento no provedor — o que o estorno usa. Único.</summary>
    public string? IdDoPagamento { get; private set; }

    /// <summary>Quando o provedor confirmou, em UTC.</summary>
    public DateTime? PagaEm { get; private set; }

    /// <summary>Quanto foi devolvido, em centavos.</summary>
    public long? ValorEstornadoEmCentavos { get; private set; }

    /// <summary>Quando o suporte estornou, em UTC.</summary>
    public DateTime? EstornadaEm { get; private set; }

    /// <summary>Cobrança aberta, à espera do pagamento.</summary>
    /// <param name="assinaturaId">Assinatura.</param>
    /// <param name="planoId">Plano pago.</param>
    /// <param name="motivo">Ciclo ou diferença.</param>
    /// <param name="meio">Meio pedido.</param>
    /// <param name="valorEmCentavos">Valor pedido.</param>
    public static CobrancaDaAssinatura Abrir(Guid assinaturaId, Guid planoId, MotivoDaCobranca motivo, MeioDePagamento meio, long valorEmCentavos) =>
        new()
        {
            AssinaturaId = assinaturaId,
            PlanoId = planoId,
            Motivo = motivo,
            Meio = meio,
            ValorEmCentavos = valorEmCentavos,
        };

    /// <summary>Confirma o pagamento. Só sai de aberta — e de cancelada, porque o dinheiro entrou mesmo assim.</summary>
    /// <remarks>
    /// Cancelada é a cobrança que a turma deixou para trás (trocou de plano ou de meio) e pagou depois: o pagamento
    /// é registrado para o histórico e para o estorno, e quem chama decide o efeito na assinatura.
    /// </remarks>
    /// <param name="idDoPagamento">Id do pagamento no provedor.</param>
    /// <param name="valorEmCentavos">O que foi creditado; nulo mantém o pedido.</param>
    /// <param name="agoraUtc">Momento da confirmação.</param>
    public Result Pagar(string idDoPagamento, long? valorEmCentavos, DateTime agoraUtc)
    {
        if (Situacao is not (SituacaoDaCobrancaDoPlano.Aberta or SituacaoDaCobrancaDoPlano.Cancelada))
            return Result.Falha(Erro.Conflito("assinatura.cobranca_ja_paga", "Esta cobrança já foi paga."));

        Situacao = SituacaoDaCobrancaDoPlano.Paga;
        IdDoPagamento = idDoPagamento;
        ValorEmCentavos = valorEmCentavos ?? ValorEmCentavos;
        PagaEm = agoraUtc;
        Url = null;

        return Result.Ok();
    }

    /// <summary>Deixa de valer: a turma trocou de plano, de meio ou cancelou antes de pagar.</summary>
    public void Cancelar()
    {
        if (Situacao == SituacaoDaCobrancaDoPlano.Aberta)
            Situacao = SituacaoDaCobrancaDoPlano.Cancelada;
    }

    /// <summary>Registra a devolução. Uma por cobrança.</summary>
    /// <param name="valorEmCentavos">Quanto voltou.</param>
    /// <param name="agoraUtc">Momento do estorno.</param>
    public Result Estornar(long valorEmCentavos, DateTime agoraUtc)
    {
        if (Situacao != SituacaoDaCobrancaDoPlano.Paga)
            return Result.Falha(Erro.Conflito("estorno.cobranca_nao_paga", "Só um pagamento confirmado, e ainda não estornado, pode ser estornado."));

        Situacao = SituacaoDaCobrancaDoPlano.Estornada;
        ValorEstornadoEmCentavos = valorEmCentavos;
        EstornadaEm = agoraUtc;

        return Result.Ok();
    }
}

/// <summary>Por que a cobrança existe. Gravado como texto.</summary>
public enum MotivoDaCobranca
{
    /// <summary>Um ciclo do plano — a contratação ou a renovação.</summary>
    Ciclo,

    /// <summary>A diferença proporcional da subida de plano no meio do ciclo (P4).</summary>
    Diferenca,
}

/// <summary>Em que pé está uma cobrança do plano. Gravado como texto.</summary>
public enum SituacaoDaCobrancaDoPlano
{
    /// <summary>Esperando o pagamento.</summary>
    Aberta,

    /// <summary>Paga.</summary>
    Paga,

    /// <summary>Não vale mais: a turma trocou de plano ou de meio antes de pagar.</summary>
    Cancelada,

    /// <summary>Paga e devolvida pelo suporte.</summary>
    Estornada,
}
