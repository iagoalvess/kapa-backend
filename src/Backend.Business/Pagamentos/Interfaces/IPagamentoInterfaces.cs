using Backend.Business.Abstractions;
using Backend.Business.Arquivos.Models;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Pagamentos.Models;

namespace Backend.Business.Pagamentos.Interfaces;

/// <summary>
/// O caminho do dinheiro da turma, do lado do formando: vê o extrato, paga pelo PIX e avisa. A
/// conferência, a baixa à mão e o estorno ficam em <see cref="ITesourariaService"/>.
/// </summary>
/// <remarks>
/// "Dono da parcela" é regra daqui, não da política: a política garante que é membro da turma, e o
/// service que a parcela é dele. Parcela de outro responde 404, não 403 — 403 confirmaria que existe.
/// </remarks>
public interface IPagamentoService
{
    /// <summary>O extrato do próprio formando.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    Task<Result<ExtratoDoFormando>> ObterExtrato(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Quantas parcelas próprias venceram sem o formando ter avisado o pagamento.</summary>
    /// <remarks>
    /// O selo do menu, que está em toda tela: por isso é um número, e não o extrato inteiro filtrado
    /// por quem chama.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    Task<Result<int>> ContarVencidasSemAviso(Guid formaturaId, Guid usuarioId, CancellationToken ct = default);

    /// <summary>Uma parcela, com o valor do dia. O dono, ou a gestão.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    /// <param name="parcelaId">Parcela.</param>
    Task<Result<ParcelaResumo>> ObterParcela(Guid formaturaId, Guid usuarioId, Guid parcelaId, CancellationToken ct = default);

    /// <summary>A cobrança da parcela: o valor do dia e os meios que a turma aceita. O dono, ou a tesouraria.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    /// <param name="parcelaId">Parcela.</param>
    Task<Result<CobrancaDaParcela>> GerarCobranca(Guid formaturaId, Guid usuarioId, Guid parcelaId, CancellationToken ct = default);

    /// <summary>
    /// A cobrança de várias parcelas de uma vez: a soma do que elas cobram hoje, pelos mesmos meios. Só o dono.
    /// </summary>
    /// <remarks>
    /// O passo que faltava no "paguei vários meses de uma vez": até 17/09/2026 o formando só podia
    /// avisar o lote, e tinha que somar as parcelas e montar o PIX por fora. As parcelas passam pelas
    /// mesmas conferências de <see cref="Informar"/>, e a distribuição continua sendo dela — este
    /// método não grava nada.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    /// <param name="parcelaIds">Parcelas que o pagamento vai cobrir, ao menos uma.</param>
    Task<Result<CobrancaDaParcela>> GerarCobrancaDeVarias(
        Guid formaturaId,
        Guid usuarioId,
        IReadOnlyList<Guid> parcelaIds,
        CancellationToken ct = default
    );

    /// <summary>
    /// Paga uma ou várias parcelas no cartão, pelo Mercado Pago da turma (Sprint 39): a baixa chega sozinha, sem
    /// aviso. Só o dono.
    /// </summary>
    /// <remarks>
    /// 409 <c>pagamento.cartao_desligado</c> com o cartão desligado na turma; <c>pagamento.valor_mudou</c> quando o
    /// valor não é o que a tela mostrou — nada é cobrado; <c>pagamento.cartao_recusado</c> quando o cartão não passou.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem paga.</param>
    /// <param name="dados">Parcelas, cartão tokenizado e o valor que a tela mostrou.</param>
    /// <returns>Pago, ou em análise — a parcela muda sozinha quando o Mercado Pago decidir.</returns>
    Task<Result<SituacaoDoCartao>> PagarNoCartao(Guid formaturaId, Guid usuarioId, PagamentoNoCartao dados, CancellationToken ct = default);

    /// <summary>
    /// O "já paguei": grava os informes pendentes e não muda parcela nenhuma. Só o dono.
    /// </summary>
    /// <remarks>
    /// Várias parcelas porque um PIX só costuma cobrir vários meses — é o formando atrasado se
    /// acertando, e obrigá-lo a rachar o valor em três avisos é atrito no pior momento possível
    /// (revisão de 17/09/2026). O valor informado é distribuído da parcela mais antiga para a mais
    /// nova, cada uma até o que ela cobra; o que sobrar fica no último aviso, e a tesouraria o vê em
    /// Divergências. Um comprovante só, compartilhado — é um pagamento só.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem informa.</param>
    /// <param name="parcelaIds">Parcelas cobertas pelo pagamento, ao menos uma.</param>
    /// <param name="dados">Dia e valor total do pagamento.</param>
    /// <param name="comprovante">Comprovante, se enviado.</param>
    Task<Result<IReadOnlyList<ParcelaResumo>>> Informar(
        Guid formaturaId,
        Guid usuarioId,
        IReadOnlyList<Guid> parcelaIds,
        NovoInforme dados,
        NovoArquivo? comprovante,
        CancellationToken ct = default
    );
}

/// <summary>
/// O recibo de cada baixa (Sprint 22): a prova do que entrou, remontada a cada pedido a partir do que a baixa gravou.
/// </summary>
public interface IReciboService
{
    /// <summary>
    /// O recibo de um recebimento em PDF, gerado na hora (Sprint 22). O próprio formando, ou a gestão.
    /// </summary>
    /// <remarks>
    /// Recebimento de outro formando responde 404, e não 403; o estornado responde 409
    /// <c>pagamento.recebimento_estornado</c>. A gestão recebe o CPF mascarado.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem pede.</param>
    /// <param name="recebimentoId">Recebimento.</param>
    Task<Result<ArquivoParaDownload>> Obter(Guid formaturaId, Guid usuarioId, Guid recebimentoId, CancellationToken ct = default);
}

/// <summary>
/// O lado da tesouraria no caminho do dinheiro: confere os avisos em lote, recusa, baixa à mão, estorna
/// e acompanha as divergências.
/// </summary>
/// <remarks>
/// Separado de <see cref="IPagamentoService"/>, que é o lado do formando: são telas, políticas e
/// dependências diferentes — a baixa, a auditoria e os e-mails de recusa e estorno só existem daqui.
/// </remarks>
public interface ITesourariaService
{
    /// <summary>A fila da tesouraria: os pendentes do mais antigo ao mais novo; os conferidos, dos mais recentes.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Situação, período do pagamento informado e busca.</param>
    Task<Result<PaginaDe<InformeNaFila>>> ListarInformes(PaginacaoRequest paginacao, FiltroDeInformes filtro, CancellationToken ct = default);

    /// <summary>O comprovante de um informe, para a tesouraria abrir.</summary>
    /// <param name="informeId">Informe.</param>
    Task<Result<ArquivoParaDownload>> BaixarComprovante(Guid informeId, CancellationToken ct = default);

    /// <summary>Confirma o lote numa transação: cada informe baixa a parcela com o valor recebido.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem confirma.</param>
    /// <param name="enderecoIp">De onde confirma.</param>
    /// <param name="lote">Informes e valores recebidos.</param>
    Task<Result<ResultadoDaConferencia>> Confirmar(
        Guid formaturaId,
        Guid usuarioId,
        string? enderecoIp,
        ConfirmarInformes lote,
        CancellationToken ct = default
    );

    /// <summary>Recusa um informe, com motivo, e avisa o formando. A parcela continua aberta.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem recusa.</param>
    /// <param name="informeId">Informe.</param>
    /// <param name="dados">Motivo.</param>
    Task<Result> Recusar(Guid formaturaId, Guid usuarioId, Guid informeId, RecusarInforme dados, CancellationToken ct = default);

    /// <summary>Baixa uma parcela sem informe — dinheiro, TED, quem pagou e não avisou.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem baixa.</param>
    /// <param name="enderecoIp">De onde baixa.</param>
    /// <param name="parcelaId">Parcela.</param>
    /// <param name="dados">Forma, dia e valor.</param>
    /// <param name="comprovante">Comprovante, se enviado.</param>
    Task<Result<ParcelaResumo>> BaixarManualmente(
        Guid formaturaId,
        Guid usuarioId,
        string? enderecoIp,
        Guid parcelaId,
        BaixaManual dados,
        NovoArquivo? comprovante,
        CancellationToken ct = default
    );

    /// <summary>Desfaz a baixa, com justificativa. A parcela volta a ser devida; o recebimento fica, estornado.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Presidente que estorna.</param>
    /// <param name="enderecoIp">De onde estorna.</param>
    /// <param name="parcelaId">Parcela.</param>
    /// <param name="dados">Justificativa.</param>
    Task<Result<ParcelaResumo>> Estornar(
        Guid formaturaId,
        Guid usuarioId,
        string? enderecoIp,
        Guid parcelaId,
        EstornarBaixa dados,
        CancellationToken ct = default
    );

    /// <summary>
    /// Cancela uma parcela específica, com justificativa e auditoria (Sprint 42, decisão 8); o que já tinha entrado nela
    /// vai para a lista "a devolver".
    /// </summary>
    /// <remarks>
    /// 409 <c>pagamento.parcela_paga</c> se está paga (estorne antes), <c>pagamento.informe_pendente</c> com aviso do
    /// formando, <c>pagamento.parcela_nao_aberta</c> se já foi cancelada.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="usuarioId">Quem cancela.</param>
    /// <param name="parcelaId">Parcela.</param>
    /// <param name="dados">Justificativa.</param>
    Task<Result<ParcelaResumo>> Cancelar(Guid formaturaId, Guid usuarioId, Guid parcelaId, CancelarParcela dados, CancellationToken ct = default);

    /// <summary>As baixas com valor recebido diferente do devido, das mais recentes.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="busca">Trecho do nome do formando.</param>
    Task<Result<PaginaDe<Divergencia>>> ListarDivergencias(PaginacaoRequest paginacao, string? busca = null, CancellationToken ct = default);
}

/// <summary>
/// Informes de pagamento da formatura selecionada.
/// </summary>
/// <remarks>Isolados pelo filtro global: nenhum método recebe a formatura.</remarks>
public interface IInformeRepository
{
    /// <summary>A parcela de cada informe — para travar as parcelas antes de ler os informes.</summary>
    /// <param name="informeIds">Informes.</param>
    Task<IReadOnlyList<Guid>> ListarParcelas(IReadOnlyCollection<Guid> informeIds, CancellationToken ct = default);

    /// <summary>Os informes, rastreados para alteração.</summary>
    /// <param name="informeIds">Informes.</param>
    Task<IReadOnlyList<InformeDePagamento>> ListarParaEdicao(IReadOnlyCollection<Guid> informeIds, CancellationToken ct = default);

    /// <summary>Se alguma das parcelas tem informe esperando conferência.</summary>
    /// <remarks>Lido sob a trava das parcelas, é o que decide entre o aviso e a baixa que chegam juntos.</remarks>
    /// <param name="parcelaIds">Parcelas.</param>
    Task<bool> ExistePendente(IReadOnlyCollection<Guid> parcelaIds, CancellationToken ct = default);

    /// <summary>Quantos avisos da turma esperam conferência — o que impede trocar para a cobrança automática.</summary>
    Task<int> ContarPendentes(CancellationToken ct = default);

    /// <summary>Os avisos pendentes destas parcelas, rastreados — a baixa automática os confirma junto.</summary>
    /// <param name="parcelaIds">Parcelas.</param>
    Task<IReadOnlyList<InformeDePagamento>> ListarPendentesParaEdicao(IReadOnlyCollection<Guid> parcelaIds, CancellationToken ct = default);

    /// <summary>Uma página da fila, com a parcela de cada informe, sem o devido.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Situação, período do pagamento informado e busca.</param>
    /// <param name="hoje">Dia que separa aberta de vencida.</param>
    /// <param name="conferidosDesdeUtc">Só os conferidos a partir deste instante; nulo traz todos.</param>
    Task<PaginaDe<InformeNaFila>> Listar(
        PaginacaoRequest paginacao,
        FiltroDeInformes filtro,
        DateOnly hoje,
        DateTime? conferidosDesdeUtc = null,
        CancellationToken ct = default
    );

    /// <summary>O comprovante do informe; nulo se o informe não existir aqui ou não tiver.</summary>
    /// <param name="informeId">Informe.</param>
    Task<ComprovanteDoInforme?> ObterComprovante(Guid informeId, CancellationToken ct = default);

    /// <summary>Marca um informe novo para inclusão.</summary>
    /// <param name="informe">Informe.</param>
    Task Adicionar(InformeDePagamento informe, CancellationToken ct = default);
}

/// <summary>
/// Recebimentos — as entradas no caixa — da formatura selecionada.
/// </summary>
/// <remarks>Isolados pelo filtro global. Não há método para remover: o estorno marca, não apaga.</remarks>
public interface IRecebimentoRepository
{
    /// <summary>A <b>última</b> baixa que vale para a parcela, rastreada; nula se ela não tem baixa ativa.</summary>
    /// <remarks>
    /// A mais recente porque desde 17/09/2026 uma parcela pode ter várias: o pagamento parcial não a
    /// fecha, e estornar é desfazer a última entrada, não todas.
    /// </remarks>
    /// <param name="parcelaId">Parcela.</param>
    Task<Recebimento?> ObterAtivoParaEdicao(Guid parcelaId, CancellationToken ct = default);

    /// <summary>
    /// A baixa ativa que a cobrança do Mercado Pago fez na parcela, rastreada (Sprint 42, F3); nula se não há.
    /// </summary>
    /// <remarks>
    /// Baixa de antes do vínculo com a cobrança não tem <c>CobrancaId</c>. Só quando <b>nenhuma</b> baixa aponta para a
    /// cobrança — nem a já estornada — ela é dessas, e a resposta é a de antes: a última ativa da parcela com a forma
    /// dela e sem cobrança. Com o vínculo gravado, a ausência quer dizer que a baixa já foi desfeita, e a manual feita
    /// depois fica.
    /// </remarks>
    /// <param name="parcelaId">Parcela.</param>
    /// <param name="cobrancaId">Cobrança.</param>
    /// <param name="forma">A forma que a cobrança grava, para a baixa sem vínculo.</param>
    Task<Recebimento?> ObterAtivoDaCobrancaParaEdicao(Guid parcelaId, Guid cobrancaId, FormaDePagamento forma, CancellationToken ct = default);

    /// <summary>
    /// Se a cobrança ainda tem outra baixa ativa além desta — o estorno à mão só encerra a cobrança quando desfaz a última.
    /// </summary>
    /// <param name="cobrancaId">Cobrança.</param>
    /// <param name="recebimentoId">A baixa que está sendo estornada, ainda ativa no banco.</param>
    Task<bool> ExisteOutroAtivoDaCobranca(Guid cobrancaId, Guid recebimentoId, CancellationToken ct = default);

    /// <summary>A cobrança do Mercado Pago da última baixa ativa da parcela, sem rastrear; nula na baixa manual.</summary>
    /// <remarks>Lida antes da transação: quem estorna trava a cobrança antes da parcela, na ordem do aviso do Mercado Pago.</remarks>
    /// <param name="parcelaId">Parcela.</param>
    Task<Guid?> ObterCobrancaDaBaixaAtiva(Guid parcelaId, CancellationToken ct = default);

    /// <summary>Uma página das baixas ativas com recebido diferente do devido, das mais recentes.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="hoje">Dia que separa aberta de vencida.</param>
    /// <param name="busca">Trecho do nome do formando.</param>
    Task<PaginaDe<Divergencia>> ListarDivergencias(PaginacaoRequest paginacao, DateOnly hoje, string? busca = null, CancellationToken ct = default);

    /// <summary>O que o recibo do recebimento imprime; nulo se ele não existir nesta turma.</summary>
    /// <remarks>Traz também o estornado: quem decide o que fazer com ele é o service.</remarks>
    /// <param name="recebimentoId">Recebimento.</param>
    /// <param name="hoje">Dia que separa aberta de vencida, na parcela.</param>
    Task<DadosDoRecibo?> ObterParaRecibo(Guid recebimentoId, DateOnly hoje, CancellationToken ct = default);

    /// <summary>Marca um recebimento novo para inclusão.</summary>
    /// <param name="recebimento">Recebimento.</param>
    Task Adicionar(Recebimento recebimento, CancellationToken ct = default);
}

/// <summary>
/// A lista "a devolver" da tesouraria (Sprint 42): crédito de pedido, parcial de parcela cancelada e pago sem parcela.
/// </summary>
/// <remarks>O Kapa não devolve dinheiro: a comissão devolve fora e registra aqui (decisão 1).</remarks>
public interface IValoresADevolverService
{
    /// <summary>Uma página da lista, dos mais antigos — é a ordem de quem está esperando.</summary>
    /// <param name="paginacao">Página pedida.</param>
    /// <param name="filtro">Situação e busca.</param>
    Task<Result<PaginaDe<ValorADevolverNaLista>>> Listar(PaginacaoRequest paginacao, FiltroDeValoresADevolver filtro, CancellationToken ct = default);

    /// <summary>
    /// A comissão fez o PIX de volta: com o comprovante, o valor sai da lista e a saída entra no caixa como despesa paga.
    /// </summary>
    /// <remarks>Sem comprovante, 400 <c>pagamento.comprovante_obrigatorio</c>; fora da lista, 409 <c>pagamento.valor_nao_a_devolver</c>.</remarks>
    /// <param name="usuarioId">Quem registra.</param>
    /// <param name="id">Valor a devolver.</param>
    /// <param name="comprovante">Comprovante do PIX.</param>
    Task<Result<ValorADevolverNaLista>> Devolver(Guid usuarioId, Guid id, NovoArquivo? comprovante, CancellationToken ct = default);

    /// <summary>A comissão resolveu o pago sem parcela — devolveu no painel do Mercado Pago ou lançou como outra receita.</summary>
    /// <remarks>Crédito e parcial não fecham assim: voltam por PIX, com comprovante (409 <c>pagamento.devolucao_exige_comprovante</c>).</remarks>
    /// <param name="usuarioId">Quem fecha.</param>
    /// <param name="id">Valor a devolver.</param>
    /// <param name="dados">O que foi feito.</param>
    Task<Result<ValorADevolverNaLista>> Fechar(Guid usuarioId, Guid id, FecharValorADevolver dados, CancellationToken ct = default);
}

/// <summary>Os valores a devolver da formatura selecionada.</summary>
/// <remarks>Isolados pelo filtro global.</remarks>
public interface IValorADevolverRepository
{
    /// <summary>Trava a linha até o fim da transação: dois cliques em "devolvido" lançam uma saída só.</summary>
    /// <param name="id">Valor a devolver.</param>
    Task<ValorADevolver?> Travar(Guid id, CancellationToken ct = default);

    /// <summary>Os que ainda esperam a comissão numa parcela, rastreados.</summary>
    /// <param name="parcelaId">Parcela cancelada.</param>
    Task<IReadOnlyList<ValorADevolver>> ListarAbertosDaParcelaParaEdicao(Guid parcelaId, CancellationToken ct = default);

    /// <summary>Os que ainda esperam a comissão de uma cobrança do Mercado Pago, rastreados.</summary>
    /// <param name="cobrancaId">Cobrança.</param>
    Task<IReadOnlyList<ValorADevolver>> ListarAbertosDaCobrancaParaEdicao(Guid cobrancaId, CancellationToken ct = default);

    /// <summary>Uma página da lista.</summary>
    /// <param name="paginacao">Página pedida, já normalizada.</param>
    /// <param name="filtro">Situação e busca.</param>
    Task<PaginaDe<ValorADevolverNaLista>> Listar(PaginacaoRequest paginacao, FiltroDeValoresADevolver filtro, CancellationToken ct = default);

    /// <summary>Um item como a lista o mostra; nulo se não é desta turma.</summary>
    /// <param name="id">Valor a devolver.</param>
    Task<ValorADevolverNaLista?> Obter(Guid id, CancellationToken ct = default);

    /// <summary>Marca um valor novo para inclusão.</summary>
    /// <param name="valor">Valor a devolver.</param>
    Task Adicionar(ValorADevolver valor, CancellationToken ct = default);
}
