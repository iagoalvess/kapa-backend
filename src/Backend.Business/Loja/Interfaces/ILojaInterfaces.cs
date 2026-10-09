using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Festa.Models;
using Backend.Business.Legal.Models;
using Backend.Business.Loja.Models;

namespace Backend.Business.Loja.Interfaces;

/// <summary>
/// A loja pública da turma: a vitrine e a compra sem conta, e a compra pelo link (Sprint 26). A lista da Gestão é o
/// <see cref="IComprasDaLojaService"/>.
/// </summary>
/// <remarks>
/// Os métodos que recebem <c>formaturaId</c> ou <c>token</c> são anônimos: quem prova a turma é a rota da
/// loja (dado público por definição, P1) ou o link assinado da compra (decisão 10), e o service aponta o
/// escopo a partir dele.
/// </remarks>
public interface ILojaService
{
    /// <summary>A vitrine da loja; 404 <c>loja.nao_encontrada</c> se a turma não vende nada pela loja.</summary>
    /// <param name="formaturaId">A turma da rota.</param>
    Task<Result<LojaDaTurma>> AbrirLoja(Guid formaturaId, CancellationToken ct = default);

    /// <summary>
    /// Compra: reserva o estoque numa transação curta e, fora dela, emite a cobrança (decisões 7 e 8).
    /// </summary>
    /// <remarks>
    /// Esgotado responde numa leitura sem trava, <b>antes</b> da fila da turma (decisão 8); quem ainda tem
    /// chance espera a vez, relê o estoque e só então abre a transação. Fila cheia é 429 <c>loja.fila_cheia</c>.
    /// A mesma <c>chave_de_idempotencia</c> devolve a mesma compra, com o mesmo link. Esgotado é 409
    /// <c>loja.esgotado</c>; passar do limite por CPF, 409 <c>loja.limite_por_pessoa</c>; antes da abertura,
    /// 409 <c>cobranca.venda_nao_aberta</c>. Se a emissão falhar, a compra volta sem cobrança, e a tela pede de
    /// novo por <see cref="EmitirCobranca"/>.
    /// </remarks>
    /// <param name="formaturaId">A turma da rota.</param>
    /// <param name="dados">Comprador, quantidade e meio.</param>
    /// <param name="origem">IP e navegador da requisição: com a versão vigente da Política, a prova de que o comprador a leu.</param>
    Task<Result<CompraCriada>> Comprar(Guid formaturaId, DadosDaCompra dados, OrigemDoAceite origem, CancellationToken ct = default);

    /// <summary>A compra pelo link. Link errado, antigo ou de compra inexistente: o mesmo 404.</summary>
    /// <param name="token">O segredo do link.</param>
    Task<Result<CompraParaOComprador>> AbrirCompra(string token, CancellationToken ct = default);

    /// <summary>Emite de novo a cobrança da compra pendente que ficou sem documento. Idempotente.</summary>
    /// <param name="token">O segredo do link.</param>
    Task<Result<CompraParaOComprador>> EmitirCobranca(string token, CancellationToken ct = default);

    /// <summary>Paga no cartão a compra pendente que o comprador fez escolhendo o cartão (Sprint 39, P5).</summary>
    /// <remarks>
    /// 409 <c>loja.compra_nao_pendente</c> se já não espera pagamento; <c>pagamento.cartao_desligado</c> com o cartão
    /// desligado; <c>pagamento.valor_mudou</c> se o valor não é o que a tela mostrou; <c>pagamento.cartao_recusado</c>
    /// quando o cartão não passou — a reserva continua, e dá para tentar outro cartão.
    /// </remarks>
    /// <param name="token">O segredo do link.</param>
    /// <param name="dados">O cartão tokenizado e o valor mostrado.</param>
    Task<Result<CompraParaOComprador>> PagarNoCartao(string token, CartaoDaCompra dados, CancellationToken ct = default);

    /// <summary>Nomeia ou transfere um convite da compra — as regras são as do convite do formando (Sprint 21).</summary>
    /// <param name="token">O segredo do link.</param>
    /// <param name="conviteId">O convite.</param>
    /// <param name="dados">Nome, documento e e-mail do convidado.</param>
    Task<Result<MeuConvite>> NomearConvidado(string token, Guid conviteId, DadosDoConvidado dados, CancellationToken ct = default);

    /// <summary>
    /// Reenvia o link das compras deste e-mail nesta turma — e mata o anterior. Responde igual exista compra
    /// ou não (decisão 10).
    /// </summary>
    /// <param name="formaturaId">A turma da rota.</param>
    /// <param name="email">E-mail da compra.</param>
    Task<Result> ReenviarLink(Guid formaturaId, string email, CancellationToken ct = default);

    /// <summary>Pede o cancelamento de convites da compra — a Gestão aprova ou recusa (Sprint 38, P1).</summary>
    /// <param name="token">O segredo do link.</param>
    /// <param name="dados">Os convites (nulo é todos) e o motivo, opcional.</param>
    Task<Result<CompraParaOComprador>> PedirCancelamento(string token, PedidoDoComprador dados, CancellationToken ct = default);

    /// <summary>
    /// Apaga nome, e-mail e CPF do comprador (decisão 5): depois da festa, ou já na compra expirada.
    /// </summary>
    /// <param name="token">O segredo do link — que deixa de abrir.</param>
    Task<Result> ApagarDados(string token, CancellationToken ct = default);
}

/// <summary>
/// As compras da loja do lado da Gestão (Sprint 26, P5): a lista da devolução, o resumo e a planilha.
/// </summary>
public interface IComprasDaLojaService
{
    /// <summary>As compras da turma, da mais nova — a lista da devolução (P5).</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Status e busca.</param>
    Task<Result<PaginaDe<CompraNaGestao>>> Listar(PaginacaoRequest paginacao, FiltroDeCompras filtro, CancellationToken ct = default);

    /// <summary>O que vendeu e o que está preso esperando pagamento (decisão 3).</summary>
    Task<Result<ResumoDaLoja>> Resumir(CancellationToken ct = default);

    /// <summary>A lista de compras em planilha, com os contatos — para a comissão devolver (P5).</summary>
    /// <param name="filtro">Status e busca.</param>
    Task<Result<ArquivoParaDownload>> Exportar(FiltroDeCompras filtro, CancellationToken ct = default);
}

/// <summary>
/// Desfazer uma compra paga da loja: o cancelamento da Gestão, a devolução e o pedido do comprador (Sprint 38).
/// </summary>
/// <remarks>
/// Cancelar é uma operação só (decisão 1): revoga os convites, devolve o lugar ao estoque, lança o estorno da
/// receita, leva a compra à lista a devolver, avisa o comprador e grava a auditoria — tudo na mesma transação.
/// O Kapa não devolve dinheiro (decisão 2): quem faz o PIX de volta é a comissão, e marca a compra devolvida
/// com o comprovante.
/// </remarks>
public interface ICancelamentoDaCompraService
{
    /// <summary>Os convites da compra — válidos e cancelados —, para a Gestão escolher o que cancelar.</summary>
    /// <param name="compraId">A compra.</param>
    Task<Result<IReadOnlyList<ConviteDaCompra>>> ListarConvites(Guid compraId, CancellationToken ct = default);

    /// <summary>
    /// Cancela convites de uma compra paga — alguns (P4) ou todos os que ainda valem.
    /// </summary>
    /// <remarks>
    /// Idempotente pela revogação efetiva (decisão 3): convite já cancelado não devolve lugar nem estorna de
    /// novo, e cancelar outra vez o que já foi cancelado é 409 <c>loja.compra_ja_cancelada</c>. Convite com
    /// entrada na portaria é 409 <c>loja.convite_ja_validado</c> (P3); compra pendente ou expirada, 409
    /// <c>loja.compra_nao_paga</c> (P8).
    /// </remarks>
    /// <param name="compraId">A compra.</param>
    /// <param name="dados">Os convites (nulo é todos) e o motivo.</param>
    /// <param name="usuarioId">Quem cancela, da Gestão.</param>
    Task<Result<CompraCancelada>> Cancelar(Guid compraId, DadosDoCancelamento dados, Guid usuarioId, CancellationToken ct = default);

    /// <summary>A comissão fez o PIX de volta: a compra sai da lista a devolver (decisão 2).</summary>
    /// <remarks>Sem comprovante, 400 <c>loja.comprovante_obrigatorio</c>; fora da lista, 409 <c>loja.compra_nao_a_devolver</c>.</remarks>
    /// <param name="compraId">A compra.</param>
    /// <param name="comprovante">PDF ou imagem do PIX.</param>
    /// <param name="usuarioId">Quem marca, da Tesouraria.</param>
    Task<Result> MarcarDevolvida(Guid compraId, NovoArquivo? comprovante, Guid usuarioId, CancellationToken ct = default);

    /// <summary>
    /// O Mercado Pago devolveu o pagamento da compra — contestação no cartão ou devolução pelo painel (Sprint 39, P4
    /// e P5): revoga os convites, estorna a receita e a compra fica devolvida. Na transação de quem chama, sem salvar.
    /// </summary>
    /// <param name="compraId">A compra, já com o escopo na turma dela.</param>
    /// <param name="motivo">"contestação no cartão" ou "devolvido no Mercado Pago".</param>
    /// <param name="usuarioId">Em nome de quem — quem conectou o Mercado Pago.</param>
    /// <returns>O que foi desfeito, para o e-mail da comissão.</returns>
    Task<Result<string>> DevolverPeloMercadoPago(Guid compraId, string motivo, Guid usuarioId, CancellationToken ct = default);

    /// <summary>O comprador pede o cancelamento pelo link (P1). Com um aberto, fica o mesmo (decisão 5).</summary>
    /// <remarks>Quem chama é a loja, depois de conferir o link e apontar o escopo para a turma da compra.</remarks>
    /// <param name="compra">A compra do link.</param>
    /// <param name="dados">Os convites e o motivo, opcional.</param>
    Task<Result> Pedir(CompraDeConvite compra, PedidoDoComprador dados, CancellationToken ct = default);

    /// <summary>Os pedidos de cancelamento abertos, do mais antigo.</summary>
    Task<Result<IReadOnlyList<PedidoNaGestao>>> ListarPedidos(CancellationToken ct = default);

    /// <summary>Aprova o pedido: é o cancelamento da Gestão, com o pedido como origem.</summary>
    /// <remarks>Pedido já respondido é 409 <c>loja.pedido_ja_respondido</c>.</remarks>
    /// <param name="pedidoId">O pedido.</param>
    /// <param name="usuarioId">Quem aprova.</param>
    Task<Result<CompraCancelada>> Aprovar(Guid pedidoId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Recusa o pedido, com motivo: os convites continuam valendo, e o comprador é avisado.</summary>
    /// <param name="pedidoId">O pedido.</param>
    /// <param name="motivo">Por que.</param>
    /// <param name="usuarioId">Quem recusa.</param>
    Task<Result> Recusar(Guid pedidoId, string motivo, Guid usuarioId, CancellationToken ct = default);
}

/// <summary>
/// As compras da loja da formatura selecionada.
/// </summary>
/// <remarks>
/// Isoladas pelo filtro global, menos os métodos <c>DeTodasAsFormaturas</c>: o link da compra e o job chegam
/// sem turma, e só descobrem nela qual é. O CPF é cifrado pelo contexto; o HMAC dele, que o limite por pessoa
/// procura, é coluna de sombra preenchida aqui.
/// </remarks>
public interface ICompraDeConviteRepository
{
    /// <summary>Os itens da loja no plano vigente, sem os encerrados.</summary>
    Task<IReadOnlyList<ItemDeCobranca>> ListarItensDaLoja(CancellationToken ct = default);

    /// <summary>
    /// O item e a compra já feita com esta chave, numa consulta só: é tudo o que quem perde precisa ler antes de
    /// receber "esgotado" (decisão 8). A compra repetida ganha do estoque — o F5 depois de comprar não é "esgotado".
    /// </summary>
    /// <param name="itemId">Item.</param>
    /// <param name="chave">A chave de idempotência.</param>
    Task<(ItemDeCobranca? Item, CompraDeConvite? Repetida)> ObterParaComprar(Guid itemId, Guid chave, CancellationToken ct = default);

    /// <summary>Um item, sem rastrear e sem trava — a leitura de antes da fila (decisão 8).</summary>
    /// <param name="itemId">Item.</param>
    Task<ItemDeCobranca?> ObterItem(Guid itemId, CancellationToken ct = default);

    /// <summary>
    /// Reserva unidades do item numa instrução: <c>UPDATE … SET reservados = reservados + q WHERE … AND
    /// reservados + q &lt;= estoque</c> (decisão 8).
    /// </summary>
    /// <remarks>
    /// Direto no banco, na transação de quem chama — a exceção que <c>ConviteRepository.ConsumirUso…</c> já
    /// abriu: a checagem precisa acontecer sob a trava da linha, e ler antes de gravar reabriria a janela. A
    /// linha fica presa só do <c>UPDATE</c> ao commit. Confere de novo que o item é da loja e não foi
    /// encerrado; abertura e prazo o service confere antes, pelo relógio da requisição.
    /// </remarks>
    /// <param name="itemId">Item.</param>
    /// <param name="quantidade">Unidades.</param>
    /// <returns>Se reservou; falso quando esgotou entre a leitura e aqui.</returns>
    Task<bool> ReservarNoItem(Guid itemId, int quantidade, CancellationToken ct = default);

    /// <summary>
    /// Serializa as compras com a mesma chave de idempotência até o fim da transação (<c>pg_advisory_xact_lock</c>).
    /// </summary>
    /// <remarks>Clique duplo chega em duas conexões ao mesmo tempo; a segunda espera e encontra a primeira gravada.</remarks>
    /// <param name="chave">A chave de idempotência.</param>
    Task TravarChave(Guid chave, CancellationToken ct = default);

    /// <summary>A compra com esta chave de idempotência, se houver.</summary>
    /// <param name="chave">A chave.</param>
    Task<CompraDeConvite?> ObterPorChave(Guid chave, CancellationToken ct = default);

    /// <summary>Quantos convites este CPF já tem no item, em compras pendentes e pagas (P3).</summary>
    /// <param name="itemId">Item.</param>
    /// <param name="cpf">CPF, só dígitos.</param>
    Task<int> ContarDoCpf(Guid itemId, string cpf, CancellationToken ct = default);

    /// <summary>Marca uma compra nova para inclusão, com o HMAC do CPF.</summary>
    /// <param name="compra">Compra.</param>
    Task Adicionar(CompraDeConvite compra, CancellationToken ct = default);

    /// <summary>A turma de uma compra — o link chega sem sessão.</summary>
    /// <param name="compraId">Compra.</param>
    Task<Guid?> ObterFormaturaDeTodasAsFormaturas(Guid compraId, CancellationToken ct = default);

    /// <summary>Uma compra, sem rastrear.</summary>
    /// <param name="compraId">Compra.</param>
    Task<CompraDeConvite?> Obter(Guid compraId, CancellationToken ct = default);

    /// <summary>Uma compra, rastreada.</summary>
    /// <param name="compraId">Compra.</param>
    Task<CompraDeConvite?> ObterParaEdicao(Guid compraId, CancellationToken ct = default);

    /// <summary>Uma compra travada até o fim da transação.</summary>
    /// <param name="compraId">Compra.</param>
    Task<CompraDeConvite?> Travar(Guid compraId, CancellationToken ct = default);

    /// <summary>As compras com este e-mail, rastreadas — para girar o link.</summary>
    /// <param name="email">E-mail, em minúsculas.</param>
    Task<IReadOnlyList<CompraDeConvite>> ListarDoEmailParaEdicao(string email, CancellationToken ct = default);

    /// <summary>
    /// Devolve lugares ao estoque do item — na transação de quem chama, sob a trava da compra (Sprint 38, decisão 1).
    /// </summary>
    /// <remarks>
    /// A mesma exceção de <see cref="ReservarNoItem"/>: é a linha que a abertura de vendas disputa, e
    /// <c>reservados = reservados - q</c> numa instrução não perde a reserva que entrou no meio. A garantia contra
    /// devolver duas vezes não mora aqui: <paramref name="quantidade"/> são os convites que quem chama acabou de
    /// revogar.
    /// </remarks>
    /// <param name="itemId">Item.</param>
    /// <param name="quantidade">Lugares.</param>
    Task DevolverAoItem(Guid itemId, int quantidade, CancellationToken ct = default);

    /// <summary>Os convites de uma compra, válidos e revogados, por posição e emissão.</summary>
    /// <param name="compraId">A compra.</param>
    Task<IReadOnlyList<ConviteDaCompra>> ListarConvites(Guid compraId, CancellationToken ct = default);

    /// <summary>O pedido de cancelamento aberto da compra, ou o último respondido; nulo se nunca houve.</summary>
    /// <param name="compraId">A compra.</param>
    Task<PedidoDeCancelamento?> ObterUltimoPedido(Guid compraId, CancellationToken ct = default);

    /// <summary>Um pedido de cancelamento travado até o fim da transação; nulo se não existir aqui.</summary>
    /// <param name="pedidoId">O pedido.</param>
    Task<PedidoDeCancelamento?> TravarPedido(Guid pedidoId, CancellationToken ct = default);

    /// <summary>Os pedidos abertos, com a compra, do mais antigo.</summary>
    Task<IReadOnlyList<PedidoNaGestao>> ListarPedidosAbertos(CancellationToken ct = default);

    /// <summary>Marca um pedido de cancelamento novo para inclusão.</summary>
    /// <param name="pedido">O pedido.</param>
    Task AdicionarPedido(PedidoDeCancelamento pedido, CancellationToken ct = default);

    /// <summary>Apaga o HMAC do CPF junto com os dados da compra.</summary>
    /// <param name="compra">Compra rastreada, com os dados já apagados.</param>
    void EsquecerCpf(CompraDeConvite compra);

    /// <summary>
    /// Expira a compra pendente vencida e devolve o estoque — duas instruções condicionais na transação de
    /// quem chama (decisão 7).
    /// </summary>
    /// <remarks>
    /// <c>UPDATE compras … SET status = 'Expirada' WHERE id = … AND status = 'Pendente' AND expira_em &lt; agora</c>;
    /// só se uma linha mudou, o estoque volta. Rodar duas vezes não devolve duas: a segunda não acha a compra
    /// pendente. Mesma exceção de <see cref="ReservarNoItem"/>.
    /// </remarks>
    /// <param name="compraId">Compra.</param>
    /// <param name="agora">Instante, em UTC.</param>
    /// <returns>Se expirou agora.</returns>
    Task<bool> Expirar(Guid compraId, DateTime agora, CancellationToken ct = default);

    /// <summary>As compras pendentes vencidas de todas as turmas, para o job.</summary>
    /// <param name="agora">Instante, em UTC.</param>
    /// <param name="limite">Teto por rodada.</param>
    Task<IReadOnlyList<CompraAExpirar>> ListarAExpirarDeTodasAsFormaturas(DateTime agora, int limite, CancellationToken ct = default);

    /// <summary>As compras pagas com menos convites válidos que a quantidade — a festa estava incompleta na agenda.</summary>
    /// <param name="eventoId">A festa.</param>
    Task<IReadOnlyList<CompraDeConvite>> ListarPagasSemConvite(Guid eventoId, CancellationToken ct = default);

    /// <summary>Uma página das compras, com o nome do item.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Status e busca.</param>
    Task<PaginaDe<CompraNaGestao>> Listar(PaginacaoRequest paginacao, FiltroDeCompras filtro, CancellationToken ct = default);

    /// <summary>Todas as compras do filtro, para a planilha.</summary>
    /// <param name="filtro">Status e busca.</param>
    Task<IReadOnlyList<CompraNaGestao>> ListarTodas(FiltroDeCompras filtro, CancellationToken ct = default);

    /// <summary>A conta da loja numa consulta agrupada.</summary>
    Task<ResumoDaLoja> Resumir(CancellationToken ct = default);

    /// <summary>
    /// Apaga e-mail, CPF e o IP e o navegador da leitura da Política das compras de turmas cuja festa terminou antes
    /// do dia informado (decisão 5).
    /// </summary>
    /// <remarks>
    /// <c>ExecuteUpdateAsync</c> atravessando as turmas: limpeza do worker, a mesma exceção de
    /// <c>ConviteDoEventoRepository.DescartarDocumentosDeTodasAsFormaturas</c>. O nome fica.
    /// </remarks>
    /// <param name="eventosAte">Festas com data anterior a este dia.</param>
    /// <returns>Quantas compras tiveram os dados apagados.</returns>
    Task<int> DescartarDadosDeTodasAsFormaturas(DateOnly eventosAte, CancellationToken ct = default);
}
