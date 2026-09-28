namespace Backend.Business.Cobrancas.Models;

/// <summary>
/// Um item opcional, como a tesouraria o informa.
/// </summary>
/// <remarks>
/// Compõe <see cref="DadosDoItem"/> em vez de repetir os campos dele: é o mesmo
/// <see cref="ItemDeCobranca"/>, e o validador do item continua sendo um só. Também é o que mantém
/// o snapshot da adesão intacto — <see cref="DadosDoItem"/> é congelado lá, e não podia crescer.
/// </remarks>
/// <param name="Item">Tipo, descrição, preço <b>unitário</b>, parcelas, dia e primeiro mês.</param>
/// <param name="LimitePorFormando">Cota por pessoa; nulo, sem cota (P6).</param>
/// <param name="PedidosAteDia">Último dia para pedir; nulo, sem prazo (P4).</param>
/// <param name="Estoque">Unidades existentes; nulo, sem teto (decisão 7).</param>
/// <param name="AberturaDeVendas">A partir de quando se pode pedir; nulo, aberto (decisão 8).</param>
/// <param name="ItemDaFestaId">O item da festa que este vende; nulo é o caso comum (decisão 11).</param>
/// <param name="ModoDeVenda">Vitrine do formando ou loja pública (Sprint 26, P8).</param>
/// <param name="PrecoPublicoEmCentavos">Preço na loja, se diferente; nulo é o mesmo do formando (Sprint 26, P4).</param>
public sealed record DadosDoOpcional(
    DadosDoItem Item,
    int? LimitePorFormando = null,
    DateOnly? PedidosAteDia = null,
    int? Estoque = null,
    DateTime? AberturaDeVendas = null,
    Guid? ItemDaFestaId = null,
    ModoDeVenda ModoDeVenda = ModoDeVenda.AoFormando,
    long? PrecoPublicoEmCentavos = null
);

/// <summary>
/// Um item opcional como o formando o vê na vitrine.
/// </summary>
/// <param name="Id">Item de cobrança.</param>
/// <param name="Tipo">O que cobra — é ele que a Sprint 21 lê para saber se o pagamento emite convite.</param>
/// <param name="Descricao">Nome na tela, se houver.</param>
/// <param name="ValorEmCentavos">Preço de <b>uma</b> unidade, em centavos.</param>
/// <param name="NumeroDeParcelas">O teto: o formando escolhe de 1 (à vista) até este número.</param>
/// <param name="DiaDeVencimento">Dia do vencimento.</param>
/// <param name="PrimeiroMes">
/// Mês a partir do qual as parcelas do pedido vencem. O diálogo mostra o calendário antes de
/// confirmar, e ele é o maior entre este mês e o próximo vencimento a partir de hoje — a mesma
/// regra que <c>PedidoService</c> aplica ao gravar, para o que se lê ser o que se deve.
/// </param>
/// <param name="LimitePorFormando">Cota por pessoa; nulo, sem cota.</param>
/// <param name="PedidosAteDia">Último dia para pedir; nulo, sem prazo.</param>
/// <param name="Estoque">Unidades existentes; nulo, sem teto — e aí a tela não mostra número nenhum.</param>
/// <param name="Reservados">Unidades já pedidas.</param>
/// <param name="Disponivel">Quantas ainda cabem; nulo no item sem teto.</param>
/// <param name="AberturaDeVendas">A partir de quando se pode pedir; antes dela, o cartão mostra a data.</param>
/// <param name="ItemDaFestaId">O item da festa que este vende, se houver.</param>
/// <param name="AbertoAPedido">Se o botão aparece hoje.</param>
public sealed record Opcional(
    Guid Id,
    TipoDeCobranca Tipo,
    string? Descricao,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    int DiaDeVencimento,
    DateOnly PrimeiroMes,
    int? LimitePorFormando,
    DateOnly? PedidosAteDia,
    int? Estoque,
    int Reservados,
    int? Disponivel,
    DateTime? AberturaDeVendas,
    Guid? ItemDaFestaId,
    bool AbertoAPedido
);

/// <summary>Quantas unidades um formando quer de um item, e em quantas vezes.</summary>
/// <param name="ItemDeCobrancaId">Item opcional.</param>
/// <param name="Quantidade">Quantidade <b>absoluta</b>, nunca um incremento.</param>
/// <param name="Parcelas">Em quantas vezes, até o teto do item; nulo é à vista.</param>
public sealed record DadosDoPedido(Guid ItemDeCobrancaId, int Quantidade, int? Parcelas = null);

/// <summary>
/// Um pedido como as duas telas o mostram: a do formando e a da Gestão.
/// </summary>
/// <param name="Id">Identificador.</param>
/// <param name="ItemDeCobrancaId">Item pedido.</param>
/// <param name="Tipo">Tipo do item.</param>
/// <param name="Descricao">Descrição do item, se houver.</param>
/// <param name="UsuarioId">Quem pediu.</param>
/// <param name="Nome">Nome civil do cadastro, ou o da conta.</param>
/// <param name="Quantidade">Unidades.</param>
/// <param name="Parcelas">Em quantas vezes o formando escolheu pagar.</param>
/// <param name="ValorUnitarioEmCentavos">Preço de uma unidade no dia do pedido.</param>
/// <param name="TotalEmCentavos">Preço vezes quantidade.</param>
/// <param name="PagoEmCentavos">O que já entrou pelas parcelas deste pedido.</param>
/// <param name="Status">Confirmado ou cancelado.</param>
/// <param name="PedidoEm">Quando foi pedido, em UTC.</param>
/// <param name="CanceladoEm">Quando foi cancelado, se foi.</param>
public sealed record PedidoResumo(
    Guid Id,
    Guid ItemDeCobrancaId,
    TipoDeCobranca Tipo,
    string? Descricao,
    Guid UsuarioId,
    string Nome,
    int Quantidade,
    int Parcelas,
    long ValorUnitarioEmCentavos,
    long TotalEmCentavos,
    long PagoEmCentavos,
    StatusDoPedido Status,
    DateTime PedidoEm,
    DateTime? CanceladoEm
)
{
    /// <summary>Se o pedido já foi quitado — é o que a Sprint 21 vai ler para emitir o convite.</summary>
    public bool Quitado => PagoEmCentavos >= TotalEmCentavos;
}

/// <summary>Filtros da lista de pedidos da turma.</summary>
/// <param name="ItemDeCobrancaId">Só os deste item.</param>
/// <param name="Status">Só nesta situação.</param>
/// <param name="Busca">Trecho do nome da conta ou do nome civil de quem pediu.</param>
/// <param name="Quitado">Só os já pagos por inteiro (<c>true</c>) ou só os que ainda devem (<c>false</c>) — a mesma conta de <see cref="PedidoResumo.Quitado"/>.</param>
public sealed record FiltroDePedidos(Guid? ItemDeCobrancaId = null, StatusDoPedido? Status = null, string? Busca = null, bool? Quitado = null);

/// <summary>
/// A conta aberta de um item na faixa da tela de Pedidos: <c>80 · 41 pagos · 27 aguardando · 12 livres</c>.
/// </summary>
/// <remarks>
/// "Restam 12" quer dizer reservados, não pagos, e é por isso que a Gestão vê a conta inteira: sem
/// baixa automática, a diferença entre pedido e dinheiro é o que a tesouraria precisa enxergar.
/// </remarks>
/// <param name="ItemDeCobrancaId">Item.</param>
/// <param name="Tipo">Tipo do item.</param>
/// <param name="Descricao">Descrição do item, se houver.</param>
/// <param name="Pedidos">Pedidos confirmados.</param>
/// <param name="Unidades">Unidades confirmadas — o número que a comissão leva ao fornecedor.</param>
/// <param name="UnidadesQuitadas">Unidades de pedidos já quitados.</param>
/// <param name="Estoque">Teto do item; nulo, sem teto.</param>
/// <param name="Disponivel">Quantas ainda cabem; nulo, sem teto.</param>
/// <param name="TotalEmCentavos">Preço vezes unidades confirmadas.</param>
/// <param name="PagoEmCentavos">O que já entrou pelas parcelas dos pedidos.</param>
public sealed record ResumoDoItemPedido(
    Guid ItemDeCobrancaId,
    TipoDeCobranca Tipo,
    string? Descricao,
    int Pedidos,
    int Unidades,
    int UnidadesQuitadas,
    int? Estoque,
    int? Disponivel,
    long TotalEmCentavos,
    long PagoEmCentavos
);

/// <summary>O que a tesouraria informa ao cancelar um pedido já pago (P5).</summary>
/// <remarks>
/// O crédito é uma parcela negativa <b>do próprio pedido</b>, e não um item novo no plano: item de
/// plano alcançaria a turma inteira, que é o buraco que esta sprint existe para fechar. O valor é
/// digitado, não calculado — reter parte é lançar um crédito menor que o pago, e a saída do
/// dinheiro é uma despesa no caixa, lançada quando o PIX de volta acontece.
/// </remarks>
/// <param name="CreditoEmCentavos">Quanto devolver, em centavos. Zero ou ausente: sem crédito.</param>
public sealed record CancelamentoDePedido(long CreditoEmCentavos = 0);

/// <summary>Um pedido de convite extra quitado, e quantos convites ele já tem.</summary>
/// <param name="Pedido">O pedido.</param>
/// <param name="ConvitesValidos">Convites válidos dele no evento.</param>
public sealed record PedidoDeConviteQuitado(Pedido Pedido, int ConvitesValidos);
