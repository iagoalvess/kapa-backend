using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Festa.Models;
using Backend.Business.Loja.Models;

namespace Backend.Business.Loja.Interfaces;

/// <summary>
/// A loja pública da turma: a vitrine e a compra sem conta, a compra pelo link e a lista da Gestão (Sprint 26).
/// </summary>
/// <remarks>
/// Os métodos que recebem <c>formaturaId</c> ou <c>token</c> são anônimos: quem prova a turma é a rota da
/// loja (dado público por definição, P1) ou o link assinado da compra (decisão 10), e o service aponta o
/// escopo a partir dele. Os da Gestão usam a turma da sessão.
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
    Task<Result<CompraCriada>> Comprar(Guid formaturaId, DadosDaCompra dados, CancellationToken ct = default);

    /// <summary>A compra pelo link. Link errado, antigo ou de compra inexistente: o mesmo 404.</summary>
    /// <param name="token">O segredo do link.</param>
    Task<Result<CompraParaOComprador>> AbrirCompra(string token, CancellationToken ct = default);

    /// <summary>Emite de novo a cobrança da compra pendente que ficou sem documento. Idempotente.</summary>
    /// <param name="token">O segredo do link.</param>
    Task<Result<CompraParaOComprador>> EmitirCobranca(string token, CancellationToken ct = default);

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

    /// <summary>
    /// Apaga nome, e-mail e CPF do comprador (decisão 5): depois da festa, ou já na compra expirada.
    /// </summary>
    /// <param name="token">O segredo do link — que deixa de abrir.</param>
    Task<Result> ApagarDados(string token, CancellationToken ct = default);

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
    /// Apaga e-mail e CPF das compras de turmas cuja festa terminou antes do dia informado (decisão 5).
    /// </summary>
    /// <remarks>
    /// <c>ExecuteUpdateAsync</c> atravessando as turmas: limpeza do worker, a mesma exceção de
    /// <c>ConviteDoEventoRepository.DescartarDocumentosDeTodasAsFormaturas</c>. O nome fica.
    /// </remarks>
    /// <param name="eventosAte">Festas com data anterior a este dia.</param>
    /// <returns>Quantas compras tiveram os dados apagados.</returns>
    Task<int> DescartarDadosDeTodasAsFormaturas(DateOnly eventosAte, CancellationToken ct = default);
}
