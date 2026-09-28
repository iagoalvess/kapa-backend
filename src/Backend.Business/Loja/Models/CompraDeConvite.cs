using Backend.Business.Abstractions;
using Backend.Business.Pagamentos.Models;
using Backend.Business.Recebimentos.Models;

namespace Backend.Business.Loja.Models;

/// <summary>Em que ponto está uma compra da loja. Gravado como texto.</summary>
public enum StatusDaCompra
{
    /// <summary>Reservou o estoque e espera o pagamento até <see cref="CompraDeConvite.ExpiraEm"/>.</summary>
    Pendente,

    /// <summary>O Mercado Pago confirmou: os convites foram emitidos.</summary>
    Paga,

    /// <summary>Não foi paga a tempo: o estoque voltou.</summary>
    Expirada,

    /// <summary>
    /// Pagou depois de expirar e não havia mais lugar (decisão 9): o dinheiro entrou e não há convite. Vai
    /// para a lista de devolução da comissão (P5) — nunca fica paga e sem convite em silêncio.
    /// </summary>
    ADevolver,
}

/// <summary>
/// Uma compra de convite pela loja pública da turma (Sprint 26, decisão 2).
/// </summary>
/// <remarks>
/// Não é pedido e não é parcela: o comprador não tem vínculo, adesão nem conta. O que a une ao resto do Kapa
/// é o item (o mesmo estoque do pedido do formando, decisão 1), a cobrança do Mercado Pago (a mesma
/// <see cref="CobrancaBancaria"/> da Sprint 25) e, depois de paga, uma outra receita da turma (decisão 4).
/// <para>
/// O comprador é titular de dado sem conta (decisão 5): e-mail e CPF valem até 30 dias depois da festa, como
/// o documento do convidado (Sprint 21, P5.1), e ele pode pedir a exclusão antes pelo link da compra. O CPF
/// é cifrado; o limite por pessoa (P3) procura pelo HMAC dele, que é coluna do banco, não da entidade.
/// </para>
/// </remarks>
public class CompraDeConvite : EntidadeDaFormatura
{
    /// <summary>Quanto tempo a reserva do PIX dinâmico dura: os 30 minutos que o Mercado Pago exige de mínimo (decisão 3).</summary>
    public static readonly TimeSpan ReservaDoPix = TimeSpan.FromMinutes(30);

    /// <summary>O item vendido.</summary>
    public Guid ItemDeCobrancaId { get; private set; }

    /// <summary>Quantos convites.</summary>
    public int Quantidade { get; private set; }

    /// <summary>O preço de uma unidade na hora da compra.</summary>
    public long ValorUnitarioEmCentavos { get; private set; }

    /// <summary>O total cobrado.</summary>
    public long ValorEmCentavos { get; private set; }

    /// <summary>Como o comprador paga.</summary>
    public MeioDePagamento Meio { get; private set; }

    /// <summary>Situação.</summary>
    public StatusDaCompra Status { get; private set; }

    /// <summary>Nome de quem comprou. Nulo depois da exclusão.</summary>
    public string? NomeDoComprador { get; private set; }

    /// <summary>E-mail de quem comprou — para onde vai o link. Nulo depois da exclusão ou do descarte.</summary>
    public string? Email { get; private set; }

    /// <summary>CPF de quem comprou, só dígitos, cifrado no banco. Nulo depois da exclusão ou do descarte.</summary>
    public string? Cpf { get; private set; }

    /// <summary>
    /// O id que a tela sorteia ao abrir o formulário (decisão 7): a mesma chave devolve a mesma compra, e F5
    /// ou clique duplo não reservam duas vezes.
    /// </summary>
    public Guid ChaveDeIdempotencia { get; private set; }

    /// <summary>Até quando a reserva vale sem pagamento, em UTC — quem calcula é o meio (decisão 3).</summary>
    public DateTime ExpiraEm { get; private set; }

    /// <summary>Quando o pagamento foi confirmado, em UTC.</summary>
    public DateTime? PagaEm { get; private set; }

    /// <summary>O que o Mercado Pago disse que entrou.</summary>
    public long? ValorPagoEmCentavos { get; private set; }

    /// <summary>O CPF de quem pagou, se o Mercado Pago informar — o sinal da P6. Cifrado no banco.</summary>
    public string? CpfDoPagador { get; private set; }

    /// <summary>
    /// A geração do link de acesso (decisão 10). O link é assinado com ela: pedir o reenvio sobe o número e
    /// mata o link anterior, sem guardar token nenhum.
    /// </summary>
    public int VersaoDoLink { get; private set; } = 1;

    /// <summary>A outra receita que a compra paga virou (decisão 4, P9).</summary>
    public Guid? OutraReceitaId { get; private set; }

    /// <summary>Quando e-mail e CPF foram apagados — descarte depois da festa ou pedido do comprador.</summary>
    public DateTime? DadosApagadosEm { get; private set; }

    /// <summary>Construtor do EF.</summary>
    protected CompraDeConvite() { }

    /// <summary>Uma compra nova, pendente.</summary>
    /// <param name="dados">Dados já validados e normalizados.</param>
    /// <param name="valorUnitarioEmCentavos">O preço da loja agora.</param>
    /// <param name="expiraEm">Fim da reserva, calculado pelo meio.</param>
    public CompraDeConvite(DadosDaCompra dados, long valorUnitarioEmCentavos, DateTime expiraEm)
    {
        ItemDeCobrancaId = dados.ItemDeCobrancaId;
        Quantidade = dados.Quantidade;
        ValorUnitarioEmCentavos = valorUnitarioEmCentavos;
        ValorEmCentavos = valorUnitarioEmCentavos * dados.Quantidade;
        Meio = dados.Meio;
        NomeDoComprador = dados.Nome.Trim();
        Email = dados.Email.Trim().ToLowerInvariant();
        Cpf = dados.Cpf;
        ChaveDeIdempotencia = dados.ChaveDeIdempotencia;
        ExpiraEm = expiraEm;
        Status = StatusDaCompra.Pendente;
    }

    /// <summary>
    /// Registra o pagamento. Pendente vira paga; expirada vira paga se <paramref name="reservouDeNovo"/>, e
    /// "a devolver" se não havia mais lugar (decisão 9).
    /// </summary>
    /// <param name="valorPagoEmCentavos">O que entrou.</param>
    /// <param name="pagaEm">Quando, em UTC.</param>
    /// <param name="cpfDoPagador">O CPF que o Mercado Pago informou, se informou.</param>
    /// <param name="reservouDeNovo">Na compra expirada, se o estoque ainda tinha lugar.</param>
    /// <returns>Se mudou — falso quando já estava paga ou a devolver.</returns>
    public bool Pagar(long valorPagoEmCentavos, DateTime pagaEm, string? cpfDoPagador, bool reservouDeNovo)
    {
        if (Status is StatusDaCompra.Paga or StatusDaCompra.ADevolver)
            return false;

        Status = Status == StatusDaCompra.Expirada && !reservouDeNovo ? StatusDaCompra.ADevolver : StatusDaCompra.Paga;
        ValorPagoEmCentavos = valorPagoEmCentavos;
        PagaEm = pagaEm;
        CpfDoPagador = cpfDoPagador;

        return true;
    }

    /// <summary>Liga a compra à receita que ela virou.</summary>
    /// <param name="outraReceitaId">A receita.</param>
    public void Receita(Guid outraReceitaId) => OutraReceitaId = outraReceitaId;

    /// <summary>Um link novo: o anterior deixa de abrir.</summary>
    public void GirarLink() => VersaoDoLink++;

    /// <summary>
    /// Apaga o que identifica o comprador: e-mail, CPF e o CPF do pagador — e o nome, quando foi ele quem pediu.
    /// </summary>
    /// <remarks>
    /// O descarte automático guarda o nome, como o do convidado (é o histórico da festa); a exclusão pedida
    /// pelo titular leva o nome também. O link morre junto: sem e-mail, não há para onde reenviá-lo.
    /// </remarks>
    /// <param name="agora">Instante, em UTC.</param>
    /// <param name="inclusiveONome">Se o titular pediu a exclusão.</param>
    public void ApagarDados(DateTime agora, bool inclusiveONome)
    {
        Email = null;
        Cpf = null;
        CpfDoPagador = null;
        DadosApagadosEm = agora;
        VersaoDoLink++;

        if (inclusiveONome)
            NomeDoComprador = null;
    }
}
