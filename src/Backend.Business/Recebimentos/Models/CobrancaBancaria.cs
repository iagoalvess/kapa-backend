using Backend.Business.Abstractions;
using Backend.Business.Pagamentos.Models;

namespace Backend.Business.Recebimentos.Models;

/// <summary>Em que ponto está uma cobrança pedida ao Mercado Pago. Gravado como texto.</summary>
public enum StatusDaCobrancaBancaria
{
    /// <summary>Gravada, e o pedido ao Mercado Pago ainda não voltou (decisão 12a).</summary>
    Emitindo,

    /// <summary>O Mercado Pago criou o pedido; o PIX vale até <see cref="CobrancaBancaria.ExpiraEm"/>.</summary>
    Emitida,

    /// <summary>O pedido falhou ou não voltou; a tela caiu no PIX estático (P7).</summary>
    Falhou,

    /// <summary>O Mercado Pago confirmou o pagamento e as parcelas foram baixadas.</summary>
    Paga,

    /// <summary>Venceu, foi cancelada ou reemitida: não vai mais ser paga.</summary>
    Encerrada,
}

/// <summary>
/// Uma cobrança feita pelo Mercado Pago da turma para uma parcela, para várias pagas juntas (Sprint 25,
/// decisão 4) ou para uma compra da loja (Sprint 26). O meio é o de <see cref="MeioDePagamento"/> (Sprint 35).
/// </summary>
/// <remarks>
/// Gravada <b>antes</b> da chamada, em <see cref="StatusDaCobrancaBancaria.Emitindo"/>, e o <c>Id</c> vai
/// ao Mercado Pago como chave de idempotência e referência externa: nova tentativa com a mesma chave não
/// emite outro documento (decisão 12a). A <see cref="Chave"/> tem índice único entre as vivas: a segunda
/// aba do mesmo formando reaproveita o documento da primeira, em vez de emitir outro.
/// <para>
/// O PIX vale até o fim do dia — amanhã o valor do dia muda —, então a chave leva as parcelas, o valor e o
/// dia; na compra da loja, só a compra.
/// </para>
/// <para><c>Parcela</c> não ganha coluna nenhuma: quem sabe da cobrança é a cobrança.</para>
/// </remarks>
public class CobrancaBancaria : EntidadeDaFormatura
{
    /// <summary>Como se paga.</summary>
    public MeioDePagamento Meio { get; private set; }

    /// <summary>
    /// A conta do Mercado Pago que emitiu — é com o token dela que o pedido se consulta. Trocar de conta (P3)
    /// não reaproveita o que a anterior emitiu: o dinheiro iria para ela, e a conta nova nem enxerga o pedido.
    /// </summary>
    public long ContaNoProvedor { get; private set; }

    /// <summary>As parcelas que o pagamento cobre. Vazio na compra da loja.</summary>
    public Guid[] ParcelaIds { get; private set; } = [];

    /// <summary>
    /// A compra da loja pública que o pagamento cobre (Sprint 26). Nula na cobrança de parcela.
    /// </summary>
    /// <remarks>
    /// A compra não é parcela de ninguém (decisão 2), mas o documento é o mesmo: com ela aqui, o aviso de
    /// pagamento, a conciliação e a idempotência da emissão servem às duas sem uma linha nova.
    /// </remarks>
    public Guid? CompraId { get; private set; }

    /// <summary>O que faz duas cobranças serem "a mesma" (<see cref="ChaveDoPix"/>, <see cref="ChaveDaCompra"/>).</summary>
    public string Chave { get; private set; } = string.Empty;

    /// <summary>O valor cobrado.</summary>
    public long ValorEmCentavos { get; private set; }

    /// <summary>Situação.</summary>
    public StatusDaCobrancaBancaria Status { get; private set; }

    /// <summary>O id do pedido no Mercado Pago; nulo até ele responder.</summary>
    public string? IdExterno { get; private set; }

    /// <summary>O BR Code do PIX.</summary>
    public string? CopiaECola { get; private set; }

    /// <summary>Até quando o PIX aceita pagamento, em UTC.</summary>
    public DateTime ExpiraEm { get; private set; }

    /// <summary>Construtor do EF.</summary>
    protected CobrancaBancaria() { }

    /// <summary>Reserva a emissão, antes de chamar o Mercado Pago.</summary>
    /// <param name="meio">Como se paga.</param>
    /// <param name="contaNoProvedor">A conta do Mercado Pago que emite.</param>
    /// <param name="parcelaIds">Parcelas cobertas.</param>
    /// <param name="valorEmCentavos">Valor, somado.</param>
    /// <param name="expiraEm">Fim da validade pedida, em UTC.</param>
    /// <param name="chave">A chave de reaproveitamento.</param>
    public CobrancaBancaria(
        MeioDePagamento meio,
        long contaNoProvedor,
        IReadOnlyCollection<Guid> parcelaIds,
        long valorEmCentavos,
        DateTime expiraEm,
        string chave
    )
    {
        Meio = meio;
        ContaNoProvedor = contaNoProvedor;
        ParcelaIds = [.. parcelaIds.Order()];
        ValorEmCentavos = valorEmCentavos;
        Chave = chave;
        ExpiraEm = expiraEm;
        Status = StatusDaCobrancaBancaria.Emitindo;
    }

    /// <summary>A cobrança de uma compra da loja — uma por compra, pela chave.</summary>
    /// <param name="meio">Como se paga.</param>
    /// <param name="contaNoProvedor">A conta do Mercado Pago que emite.</param>
    /// <param name="compraId">A compra.</param>
    /// <param name="valorEmCentavos">Valor da compra.</param>
    /// <param name="expiraEm">Fim da validade do documento, em UTC.</param>
    public static CobrancaBancaria DaCompra(MeioDePagamento meio, long contaNoProvedor, Guid compraId, long valorEmCentavos, DateTime expiraEm) =>
        new(meio, contaNoProvedor, [], valorEmCentavos, expiraEm, ChaveDaCompra(compraId)) { CompraId = compraId };

    /// <summary>A chave da cobrança de uma compra: a compra tem um documento só enquanto ele vive.</summary>
    /// <param name="compraId">A compra.</param>
    public static string ChaveDaCompra(Guid compraId) => $"compra:{compraId:N}";

    /// <summary>O Mercado Pago criou o pedido.</summary>
    /// <param name="idExterno">Id do pedido lá.</param>
    /// <param name="copiaECola">BR Code, no PIX.</param>
    public void Emitida(string idExterno, string? copiaECola)
    {
        IdExterno = idExterno;
        CopiaECola = copiaECola;
        Status = StatusDaCobrancaBancaria.Emitida;
    }

    /// <summary>O pedido não voltou. Solta o índice das vivas para a próxima tentativa.</summary>
    public void Falhou() => Status = StatusDaCobrancaBancaria.Falhou;

    /// <summary>O Mercado Pago confirmou o pagamento.</summary>
    public void Paga() => Status = StatusDaCobrancaBancaria.Paga;

    /// <summary>Não vai mais ser paga.</summary>
    public void Encerrada() => Status = StatusDaCobrancaBancaria.Encerrada;

    /// <summary>Se ainda pode ser paga: emitida e dentro da validade.</summary>
    /// <param name="agoraUtc">Agora.</param>
    public bool Pagavel(DateTime agoraUtc) => Status == StatusDaCobrancaBancaria.Emitida && ExpiraEm > agoraUtc;

    /// <summary>A chave do PIX: a conta, o dia, o valor e as parcelas em ordem.</summary>
    /// <param name="contaNoProvedor">A conta do Mercado Pago.</param>
    /// <param name="parcelaIds">Parcelas.</param>
    /// <param name="valorEmCentavos">Valor.</param>
    /// <param name="dia">Dia local da emissão.</param>
    public static string ChaveDoPix(long contaNoProvedor, IEnumerable<Guid> parcelaIds, long valorEmCentavos, DateOnly dia) =>
        $"pix:{contaNoProvedor}:{dia:yyyyMMdd}:{valorEmCentavos}:{Parcelas(parcelaIds)}";

    private static string Parcelas(IEnumerable<Guid> parcelaIds) => string.Join(',', parcelaIds.Order().Select(id => id.ToString("N")));
}
