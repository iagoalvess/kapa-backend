using System.Text.Json;
using Backend.Business.Abstractions;
using Backend.Business.Festa.Models;
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

    /// <summary>A comissão fez o PIX de volta e anexou o comprovante (Sprint 38, decisão 2): sai da lista a devolver.</summary>
    Devolvida,
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
    /// <summary>O item vendido.</summary>
    public Guid ItemDeCobrancaId { get; private set; }

    /// <summary>Quantos convites.</summary>
    public int Quantidade { get; private set; }

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
    /// Quem vai usar cada convite, informado na compra: a lista de <see cref="DadosDoConvidado"/> em JSON, cifrada no
    /// banco. A emissão copia cada um para o convite da mesma posição; depois disso a troca é pelo convite.
    /// Nula depois da exclusão ou do descarte.
    /// </summary>
    public string? Convidados { get; private set; }

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

    /// <summary>
    /// Quantos lugares da compra deixaram de valer (Sprint 38): cancelados pela Gestão, ou os que nunca houve —
    /// a compra paga sem lugar (decisão 9 da Sprint 26) nasce com todos aqui.
    /// </summary>
    public int ConvitesCancelados { get; private set; }

    /// <summary>A soma dos estornos lançados contra a receita da compra (Sprint 38, P5).</summary>
    public long ValorEstornadoEmCentavos { get; private set; }

    /// <summary>Quanto a comissão ainda tem de devolver ao comprador — zera quando ela marca devolvida (P9).</summary>
    public long ValorADevolverEmCentavos { get; private set; }

    /// <summary>Quando a comissão marcou a devolução, em UTC.</summary>
    public DateTime? DevolvidaEm { get; private set; }

    /// <summary>O comprovante do PIX de volta, no módulo de arquivos (decisão 2).</summary>
    public Guid? ComprovanteDaDevolucaoId { get; private set; }

    /// <summary>O que o comprador de fato pagou — o valor da compra, se o Mercado Pago não informou outro.</summary>
    public long ValorPago => ValorPagoEmCentavos ?? ValorEmCentavos;

    /// <summary>Quantos lugares ainda valem: os reservados da pendente, os não cancelados da paga, nenhum da expirada.</summary>
    public int LugaresValendo =>
        Status switch
        {
            StatusDaCompra.Pendente => Quantidade,
            StatusDaCompra.Expirada => 0,
            _ => Quantidade - ConvitesCancelados,
        };

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
        ValorEmCentavos = valorUnitarioEmCentavos * dados.Quantidade;
        Meio = dados.Meio;
        NomeDoComprador = dados.Nome.Trim();
        Email = dados.Email.Trim().ToLowerInvariant();
        Cpf = dados.Cpf;
        ChaveDeIdempotencia = dados.ChaveDeIdempotencia;
        Convidados = dados.Convidados.Count > 0 ? JsonSerializer.Serialize(dados.Convidados) : null;
        ExpiraEm = expiraEm;
        Status = StatusDaCompra.Pendente;
    }

    /// <summary>Os titulares informados na compra, na ordem dos convites; vazia na compra antiga ou depois da exclusão.</summary>
    public IReadOnlyList<DadosDoConvidado> Titulares() =>
        Convidados is null ? [] : JsonSerializer.Deserialize<List<DadosDoConvidado>>(Convidados) ?? [];

    /// <summary>
    /// Registra o pagamento. Pendente vira paga; expirada vira paga se <paramref name="reservouDeNovo"/>, e
    /// "a devolver" se não havia mais lugar (decisão 9).
    /// </summary>
    /// <param name="valorPagoEmCentavos">O que entrou.</param>
    /// <param name="pagaEm">Quando, em UTC.</param>
    /// <param name="cpfDoPagador">O CPF que o Mercado Pago informou, se informou.</param>
    /// <param name="reservouDeNovo">Na compra expirada, se o estoque ainda tinha lugar.</param>
    /// <returns>Se mudou — falso quando já estava paga, a devolver ou devolvida.</returns>
    public bool Pagar(long valorPagoEmCentavos, DateTime pagaEm, string? cpfDoPagador, bool reservouDeNovo)
    {
        if (Status is not (StatusDaCompra.Pendente or StatusDaCompra.Expirada))
            return false;

        Status = Status == StatusDaCompra.Expirada && !reservouDeNovo ? StatusDaCompra.ADevolver : StatusDaCompra.Paga;
        ValorPagoEmCentavos = valorPagoEmCentavos;
        PagaEm = pagaEm;
        CpfDoPagador = cpfDoPagador;

        if (Status == StatusDaCompra.ADevolver)
        {
            ConvitesCancelados = Quantidade;
            ValorADevolverEmCentavos = valorPagoEmCentavos;
        }

        return true;
    }

    /// <summary>
    /// Tira lugares da compra e calcula o estorno deles (Sprint 38, decisões 1 e 6): a compra vai para a lista a
    /// devolver.
    /// </summary>
    /// <remarks>
    /// O estorno é o pago por convite vezes os cancelados; o centavo que a divisão deixa vai no último lugar da
    /// compra, então cancelar tudo, de uma vez ou aos poucos, estorna exatamente o que entrou. A taxa do Mercado
    /// Pago não volta — ele cobrou da turma. Quem garante que <paramref name="lugares"/> não conta duas vezes o
    /// mesmo convite é o chamador: só os que ele revogou agora, sob a trava da compra (decisão 3).
    /// </remarks>
    /// <param name="lugares">Quantos lugares deixam de valer — entre 1 e <see cref="LugaresValendo"/>.</param>
    /// <returns>O valor do estorno, em centavos.</returns>
    public long Cancelar(int lugares)
    {
        if (lugares < 1 || lugares > LugaresValendo || Status is StatusDaCompra.Pendente or StatusDaCompra.Expirada)
            throw new InvalidOperationException($"Compra {Id}: não dá para cancelar {lugares} de {LugaresValendo} lugares no status {Status}.");

        ConvitesCancelados += lugares;

        var estorno = ConvitesCancelados == Quantidade ? ValorPago - ValorEstornadoEmCentavos : lugares * (ValorPago / Quantidade);

        ValorEstornadoEmCentavos += estorno;
        ValorADevolverEmCentavos += estorno;
        Status = StatusDaCompra.ADevolver;

        return estorno;
    }

    /// <summary>
    /// A comissão fez o PIX de volta (decisão 2): a compra sai da lista a devolver.
    /// </summary>
    /// <remarks>
    /// A compra paga sem lugar (decisão 9 da Sprint 26) não passou por <see cref="Cancelar"/>, e a receita dela
    /// continua inteira: o estorno dela nasce aqui, na data da devolução (P5).
    /// </remarks>
    /// <param name="agora">Instante, em UTC.</param>
    /// <param name="comprovanteId">O comprovante do PIX; nulo quando quem devolveu foi o Mercado Pago — contestação no cartão ou devolução pelo painel (Sprint 39, P4).</param>
    /// <returns>O estorno que ainda faltava lançar, em centavos — zero na compra cancelada, que já estornou.</returns>
    public long Devolver(DateTime agora, Guid? comprovanteId)
    {
        if (Status != StatusDaCompra.ADevolver)
            throw new InvalidOperationException($"Compra {Id} marcada devolvida no status {Status}.");

        var estorno = LugaresValendo == 0 ? ValorPago - ValorEstornadoEmCentavos : 0;

        ValorEstornadoEmCentavos += estorno;
        ValorADevolverEmCentavos = 0;
        DevolvidaEm = agora;
        ComprovanteDaDevolucaoId = comprovanteId;
        Status = StatusDaCompra.Devolvida;

        return estorno;
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
        Convidados = null;
        DadosApagadosEm = agora;
        VersaoDoLink++;

        if (inclusiveONome)
            NomeDoComprador = null;
    }
}
