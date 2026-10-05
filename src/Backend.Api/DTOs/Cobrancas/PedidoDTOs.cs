using Backend.Business.Cobrancas.Models;
using Backend.Business.Loja.Models;

namespace Backend.Api.DTOs.Cobrancas;

/// <summary>
/// Corpo do cadastro de um item opcional.
/// </summary>
/// <remarks>
/// É o corpo do item do plano com cinco campos a mais. O <c>valor_em_centavos</c> muda de sentido:
/// aqui ele é o preço de <b>uma unidade</b> (decisão 2), e quem multiplica pela quantidade é o
/// pedido.
/// </remarks>
/// <param name="Tipo"><c>ConviteExtra</c>, <c>Avulsa</c> ou outro — é ele que a Sprint 21 lê para saber se o pagamento emite convite.</param>
/// <param name="Descricao">Nome na tela ("Convite extra", "Kit da turma").</param>
/// <param name="ValorEmCentavos">Preço de <b>uma</b> unidade, em centavos — <c>18000</c> é R$ 180,00.</param>
/// <param name="NumeroDeParcelas">Em quantas vezes o total do pedido é dividido, de 1 a 120.</param>
/// <param name="DiaDeVencimento">De 1 a 31; no mês mais curto, vale o último dia.</param>
/// <param name="PrimeiroMes">
/// Mês a partir do qual as parcelas vencem, <c>aaaa-mm-dd</c>. O pedido nunca gera parcela vencida:
/// vale o maior entre este mês e o próximo vencimento a partir do dia do pedido.
/// </param>
/// <param name="LimitePorFormando">Cota por pessoa; ausente, sem cota (P6).</param>
/// <param name="PedidosAteDia">Último dia para pedir, <c>aaaa-mm-dd</c>; ausente, sem prazo (P4).</param>
/// <param name="Estoque">Unidades existentes; ausente, sem teto. Abaixo do já reservado, 409 <c>cobranca.estoque_menor_que_reservado</c>.</param>
/// <param name="AberturaDeVendas">A partir de quando se pode pedir; antes dela, 409 <c>cobranca.venda_nao_aberta</c>.</param>
/// <param name="ItemDaFestaId">O item da festa que este item vende. Só <c>PorFormando</c>, de pé e ainda sem opcionais (decisão 11).</param>
/// <param name="ModoDeVenda">
/// <c>AoFormando</c> (o padrão) ou <c>Publica</c> — a loja da Sprint 26, só para <c>ConviteExtra</c> e com o Mercado Pago
/// conectado (409 <c>loja.sem_mercado_pago</c>). As portas são exclusivas.
/// </param>
/// <param name="PrecoPublicoEmCentavos">Preço de uma unidade na loja; ausente, o mesmo do formando.</param>
/// <param name="UltimoVencimento">
/// Até quando a última parcela pode vencer — o teto do item e a divisão de cada pedido (Sprint 47, D28); ausente, nada
/// é conferido. Passou, 400 <c>cobranca.ultima_parcela_depois_do_limite</c>.
/// </param>
/// <param name="CancelavelAte">Último dia para o formando pedir o cancelamento do pedido; ausente, sem trava (Sprint 48, D36).</param>
public sealed record OpcionalRequestDTO(
    TipoDeCobranca Tipo,
    string? Descricao,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    int DiaDeVencimento,
    DateOnly PrimeiroMes,
    int? LimitePorFormando,
    DateOnly? PedidosAteDia,
    int? Estoque,
    DateTime? AberturaDeVendas,
    Guid? ItemDaFestaId,
    ModoDeVenda ModoDeVenda = ModoDeVenda.AoFormando,
    long? PrecoPublicoEmCentavos = null,
    DateOnly? UltimoVencimento = null,
    DateOnly? CancelavelAte = null
)
{
    /// <summary>O corpo como o service o recebe.</summary>
    public DadosDoOpcional ParaModelo() =>
        new(
            new DadosDoItem(Tipo, Descricao, ValorEmCentavos, NumeroDeParcelas, DiaDeVencimento, PrimeiroMes),
            LimitePorFormando,
            PedidosAteDia,
            Estoque,
            AberturaDeVendas,
            ItemDaFestaId,
            ModoDeVenda,
            PrecoPublicoEmCentavos,
            UltimoVencimento,
            CancelavelAte
        );
}

/// <summary>Um item opcional como o formando o vê na vitrine.</summary>
/// <param name="Id">Item de cobrança.</param>
/// <param name="Tipo">O que cobra.</param>
/// <param name="Descricao">Nome na tela, se houver.</param>
/// <param name="ValorEmCentavos">Preço de uma unidade, em centavos.</param>
/// <param name="NumeroDeParcelas">Em quantas vezes.</param>
/// <param name="DiaDeVencimento">Dia do vencimento.</param>
/// <param name="PrimeiroMes">
/// Mês a partir do qual as parcelas vencem. O calendário que o diálogo mostra antes de confirmar
/// começa no maior entre este mês e o próximo vencimento a partir de hoje — nenhuma parcela de
/// pedido nasce vencida.
/// </param>
/// <param name="LimitePorFormando">Cota por pessoa; nulo, sem cota.</param>
/// <param name="PedidosAteDia">Último dia para pedir; nulo, sem prazo.</param>
/// <param name="Estoque">Unidades existentes; nulo, sem teto — e aí a tela não mostra contagem nenhuma.</param>
/// <param name="Reservados">Unidades já pedidas.</param>
/// <param name="Disponivel">Quantas ainda cabem; nulo no item sem teto. É <b>reservado</b>, não pago.</param>
/// <param name="AberturaDeVendas">A partir de quando se pode pedir.</param>
/// <param name="ItemDaFestaId">O item da festa que este item vende, se houver.</param>
/// <param name="AbertoAPedido">Se o botão aparece hoje; falso mostra a data da abertura.</param>
/// <param name="UltimoVencimento">Até quando a última parcela do pedido pode vencer — a prévia avisa antes do pedido ser recusado.</param>
public sealed record OpcionalDTO(
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
    bool AbertoAPedido,
    DateOnly? UltimoVencimento
);

/// <summary>Corpo de um pedido.</summary>
/// <param name="ItemDeCobrancaId">Item opcional. Só no <c>POST</c> — o <c>PUT</c> já sabe qual é.</param>
/// <param name="Quantidade">Quantidade <b>absoluta</b>, nunca um incremento: repetir a chamada é um no-op.</param>
/// <param name="Parcelas">
/// Em quantas vezes pagar, de 1 até o <c>numero_de_parcelas</c> do item; ausente é à vista. Acima do
/// teto, 400 <c>cobranca.parcelas_acima_do_teto</c>. Só vale no pedido novo — o de pé mantém a dele.
/// </param>
/// <param name="Observacao">Detalhe livre — tamanho da beca, nome no convite (Sprint 48, D26). Até 300 caracteres.</param>
public sealed record PedidoRequestDTO(Guid ItemDeCobrancaId, int Quantidade, int? Parcelas = null, string? Observacao = null);

/// <summary>Corpo da mudança de quantidade.</summary>
/// <param name="Quantidade">Quantidade <b>absoluta</b>. Diminuir com parcela paga devolve 409 <c>cobranca.pedido_com_parcela_paga</c>.</param>
public sealed record QuantidadeDoPedidoRequestDTO(int Quantidade);

/// <summary>Corpo do cancelamento.</summary>
/// <param name="CreditoEmCentavos">
/// Quanto devolver ao formando, em centavos — só a tesouraria (P5). Vira uma parcela negativa do
/// próprio pedido, que abate o que ele ainda deve. Ausente ou zero: sem crédito, e o pedido encolhe
/// para o que o pagamento já cobria.
/// </param>
/// <param name="Motivo">Por que o formando pede o cancelamento — só vale na solicitação dele (Sprint 48, D8).</param>
public sealed record CancelarPedidoRequestDTO(long CreditoEmCentavos = 0, string? Motivo = null);

/// <summary>Um pedido, como o formando e a Gestão o veem.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="ItemDeCobrancaId">Item pedido.</param>
/// <param name="Tipo">Tipo do item.</param>
/// <param name="Descricao">Descrição do item, se houver.</param>
/// <param name="UsuarioId">Quem pediu.</param>
/// <param name="Nome">Nome civil do cadastro, ou o da conta.</param>
/// <param name="Quantidade">Unidades.</param>
/// <param name="Parcelas">Em quantas vezes o formando escolheu pagar.</param>
/// <param name="TotalEmCentavos">Preço vezes quantidade.</param>
/// <param name="PagoEmCentavos">O que já entrou pelas parcelas deste pedido.</param>
/// <param name="Quitado">Se o pago alcançou o total.</param>
/// <param name="Status"><c>Confirmado</c> ou <c>Cancelado</c>.</param>
/// <param name="PedidoEm">Quando foi pedido, em UTC.</param>
/// <param name="Observacao">O detalhe livre de quem pediu (Sprint 48, D26).</param>
/// <param name="CancelavelAte">Último dia para pedir o cancelamento; nulo, sem trava (D36).</param>
/// <param name="CancelamentoSolicitado">Há solicitação de cancelamento esperando a comissão (D8).</param>
public sealed record PedidoDTO(
    Guid Id,
    Guid ItemDeCobrancaId,
    TipoDeCobranca Tipo,
    string? Descricao,
    Guid UsuarioId,
    string Nome,
    int Quantidade,
    int Parcelas,
    long TotalEmCentavos,
    long PagoEmCentavos,
    bool Quitado,
    StatusDoPedido Status,
    DateTime PedidoEm,
    string? Observacao,
    DateOnly? CancelavelAte,
    bool CancelamentoSolicitado
);

/// <summary>A conta aberta de um item na faixa da tela de Pedidos.</summary>
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
public sealed record ResumoDoItemPedidoDTO(
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

/// <summary>Corpo da solicitação de cancelamento do formando (Sprint 48, D8).</summary>
/// <param name="ItemDeCobrancaId">O pacote da cesta ou o item do pedido.</param>
/// <param name="Motivo">Por que, se quiser dizer. Até 300 caracteres.</param>
public sealed record SolicitacaoDeCancelamentoRequestDTO(Guid ItemDeCobrancaId, string? Motivo);

/// <summary>Corpo da recusa de uma solicitação.</summary>
/// <param name="Motivo">Por que a comissão recusou — o formando lê. Obrigatório, até 300 caracteres.</param>
public sealed record RecusaDaSolicitacaoRequestDTO(string? Motivo);

/// <summary>Uma solicitação de cancelamento, como o formando e a comissão a veem.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="UsuarioId">Quem pediu.</param>
/// <param name="Nome">Nome civil do cadastro, ou o da conta.</param>
/// <param name="ItemDeCobrancaId">O pacote ou o item do pedido.</param>
/// <param name="Tipo">Tipo do item.</param>
/// <param name="Descricao">Descrição do item, se houver.</param>
/// <param name="Grupo">Grupo de faixas do pacote, se houver.</param>
/// <param name="PedidoId">O pedido avulso; nulo é pacote da cesta.</param>
/// <param name="Motivo">Por que o formando pediu.</param>
/// <param name="PedidoEm">Quando pediu, em UTC.</param>
/// <param name="RespostaAte">Último dia do prazo de resposta — e da suspensão das parcelas (D37).</param>
/// <param name="Status"><c>Aberto</c>, <c>Aprovado</c> ou <c>Recusado</c>.</param>
/// <param name="MotivoDaResposta">O motivo da recusa.</param>
/// <param name="RespondidoEm">Quando a comissão respondeu, em UTC.</param>
/// <param name="PagoEmCentavos">O que já entrou pelas parcelas do item — o que vai para "a devolver" se aprovar (D9).</param>
public sealed record SolicitacaoDeCancelamentoDTO(
    Guid Id,
    Guid UsuarioId,
    string Nome,
    Guid ItemDeCobrancaId,
    TipoDeCobranca Tipo,
    string? Descricao,
    string? Grupo,
    Guid? PedidoId,
    string? Motivo,
    DateTime PedidoEm,
    DateOnly RespostaAte,
    StatusDoPedidoDeCancelamento Status,
    string? MotivoDaResposta,
    DateTime? RespondidoEm,
    long PagoEmCentavos
);
