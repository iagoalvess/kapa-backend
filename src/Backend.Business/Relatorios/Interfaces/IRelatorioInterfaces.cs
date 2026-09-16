using Backend.Business.Abstractions;
using Backend.Business.Relatorios.Models;

namespace Backend.Business.Relatorios.Interfaces;

/// <summary>
/// Os dois painéis da turma: o que todo mundo lê e o que só a comissão lê.
/// </summary>
/// <remarks>
/// Nenhum número sai daqui recalculado: todos vêm de <c>ICaixaService</c> (decisão 4). O que este
/// serviço acrescenta é o que o caixa não conhece — adimplência e fornecedores.
/// <para>
/// Um painel só, e ele é público. Nada de um método que devolva tudo e deixe o filtro para quem
/// chama: é o método que um dia vai ser chamado errado, e o que vaza é nome de quem deve (decisão 1).
/// </para>
/// </remarks>
public interface IDashboardService
{
    /// <summary>O painel do formando: caixa, adimplência, gastos por categoria e por fornecedor.</summary>
    /// <remarks>Nenhum campo do retorno aponta para uma pessoa.</remarks>
    /// <param name="formaturaId">Formatura da sessão — é dela a data da colação.</param>
    Task<Result<IndicadoresPublicos>> Publico(Guid formaturaId, CancellationToken ct = default);
}

/// <summary>
/// O balancete e as exportações.
/// </summary>
/// <remarks>
/// Cada relatório existe nos dois formatos, e a divisão de esforço é por formato: a planilha sai na
/// requisição, o PDF vira solicitação para o worker. Paginar e diagramar é o que demora — montar um
/// XLSX de algumas centenas de linhas, não.
/// </remarks>
public interface IRelatorioService
{
    /// <summary>O balancete do período, consolidado.</summary>
    /// <param name="formaturaId">Formatura da sessão.</param>
    /// <param name="periodo">Intervalo pedido, já normalizado.</param>
    /// <param name="emitidoPorUsuarioId">Quem pediu — o nome dele vai na capa.</param>
    Task<Result<Balancete>> Balancete(Guid formaturaId, PeriodoDoRelatorio periodo, Guid emitidoPorUsuarioId, CancellationToken ct = default);

    /// <summary>
    /// Um relatório reduzido a título, colunas e linhas — a forma de que saem a planilha e o PDF.
    /// </summary>
    /// <remarks>
    /// É o que impede quatro relatórios em dois formatos de virarem oito implementações: monta-se a
    /// tabela uma vez, e cada formato sabe escrever uma tabela.
    /// </remarks>
    /// <param name="formaturaId">Formatura da sessão — o nome dela vai no subtítulo.</param>
    /// <param name="tipo">Qual relatório.</param>
    /// <param name="filtro">Recorte pedido, com o período já normalizado. O balancete só aceita o período.</param>
    Task<Result<TabelaDoRelatorio>> Tabela(Guid formaturaId, TipoDeRelatorio tipo, FiltroDoRelatorio filtro, CancellationToken ct = default);

    /// <summary>Agenda o PDF de um relatório e devolve na hora.</summary>
    /// <param name="tipo">Qual relatório.</param>
    /// <param name="filtro">Recorte pedido, com o período já normalizado. Vai gravado, para o worker refazê-lo.</param>
    /// <param name="solicitadaPorUsuarioId">Quem pediu.</param>
    Task<Result<SolicitacaoResumo>> Solicitar(
        TipoDeRelatorio tipo,
        FiltroDoRelatorio filtro,
        Guid solicitadaPorUsuarioId,
        CancellationToken ct = default
    );

    /// <summary>As solicitações da turma, da mais recente — status e, nas prontas, o que baixar.</summary>
    Task<Result<IReadOnlyList<SolicitacaoResumo>>> ListarSolicitacoes(CancellationToken ct = default);

    /// <summary>O que os seletores de filtro da tela oferecem.</summary>
    Task<Result<OpcoesDeFiltro>> OpcoesDeFiltro(CancellationToken ct = default);

    /// <summary>
    /// O arquivo de uma solicitação pronta.
    /// </summary>
    /// <remarks>
    /// Confere a formatura (pelo filtro global) e o papel (pela política do endpoint). Solicitação de
    /// outra turma, expirada ou ainda na fila responde a mesma coisa: 404.
    /// </remarks>
    /// <param name="id">Solicitação.</param>
    /// <param name="solicitante">Quem está pedindo.</param>
    Task<Result<Arquivos.Models.ArquivoParaDownload>> Baixar(Guid id, Guid solicitante, CancellationToken ct = default);
}

/// <summary>
/// Gera o que está na fila. Chamado pelo worker, nunca por uma requisição.
/// </summary>
/// <remarks>
/// Fica no <c>Business</c>, e não dentro do job: o job é um <c>BackgroundService</c> — ele abre o
/// escopo, respeita o intervalo e registra o log. A regra de gerar mora aqui, e é testável sem host.
/// </remarks>
public interface IGeracaoDeRelatoriosService
{
    /// <summary>O que está na fila, de qualquer turma, da mais antiga.</summary>
    /// <remarks>
    /// Devolve a formatura junto porque é ela que o job precisa para apontar o escopo seguinte — o
    /// isolamento continua valendo lá dentro.
    /// </remarks>
    /// <param name="quantidade">Teto de solicitações nesta passada.</param>
    Task<IReadOnlyList<RelatorioPendente>> ListarPendentes(int quantidade, CancellationToken ct = default);

    /// <summary>
    /// Gera uma solicitação: monta o PDF, guarda o arquivo e enfileira o e-mail.
    /// </summary>
    /// <remarks>O escopo já precisa estar apontado para a formatura dela (<c>FormaturaDoProcessamento</c>).</remarks>
    /// <param name="solicitacaoId">Solicitação.</param>
    /// <returns>Se o arquivo ficou pronto.</returns>
    Task<bool> Gerar(Guid solicitacaoId, CancellationToken ct = default);

    /// <summary>Apaga os arquivos das solicitações cujo prazo acabou.</summary>
    /// <returns>Quantas expiraram.</returns>
    Task<int> ExpirarVencidas(CancellationToken ct = default);
}

/// <summary>Uma solicitação esperando o worker.</summary>
/// <param name="SolicitacaoId">Solicitação a gerar.</param>
/// <param name="FormaturaId">Turma dela — o escopo da geração aponta para cá.</param>
public readonly record struct RelatorioPendente(Guid SolicitacaoId, Guid FormaturaId);

/// <summary>
/// As agregações que o caixa não conhece: adimplência, fornecedores, inadimplentes e as exportações.
/// </summary>
/// <remarks>
/// <b>Nada aqui repete o <c>ICaixaRepository</c></b> (decisão 4): saldo, arrecadado, gasto, quadro
/// por categoria e o fluxo mês a mês continuam saindo de lá, e um <c>grep</c> por
/// <c>SumAsync</c> nesta classe não acha nenhuma das somas que ele já faz.
/// <para>
/// Isolado pelo filtro global: nenhum método recebe a formatura.
/// </para>
/// </remarks>
public interface IRelatorioRepository
{
    /// <summary>O que já venceu e quanto disso entrou — o índice de adimplência.</summary>
    /// <param name="hoje">Dia que separa vencida de a vencer.</param>
    Task<Adimplencia> Adimplencia(DateOnly hoje, CancellationToken ct = default);

    /// <summary>O gasto por fornecedor, do maior para o menor.</summary>
    /// <param name="periodo">Intervalo do pagamento; nulo soma a turma inteira, desde sempre.</param>
    Task<IReadOnlyList<GastoPorFornecedor>> PorFornecedor(PeriodoDoRelatorio? periodo, CancellationToken ct = default);

    /// <summary>O que entrou no período, por tipo de cobrança.</summary>
    /// <param name="periodo">Intervalo do pagamento.</param>
    Task<IReadOnlyList<LinhaDeBalancete>> EntradasPorTipo(PeriodoDoRelatorio periodo, CancellationToken ct = default);

    /// <summary>O que saiu no período, por categoria de despesa.</summary>
    /// <param name="periodo">Intervalo do pagamento.</param>
    Task<IReadOnlyList<LinhaDeBalancete>> SaidasPorCategoria(PeriodoDoRelatorio periodo, CancellationToken ct = default);

    /// <summary>Entradas e saídas do período, só os totais.</summary>
    /// <remarks>O balancete já soma os dele pelas linhas; isto é para o período anterior, da comparação.</remarks>
    /// <param name="periodo">Intervalo do pagamento.</param>
    Task<TotaisDoPeriodo> Totais(PeriodoDoRelatorio periodo, CancellationToken ct = default);

    /// <summary>O movimento do período mês a mês, sem buraco entre o primeiro e o último.</summary>
    /// <remarks>
    /// Recortado pelos mesmos dias do balancete: a soma dos meses fecha com o total do período,
    /// inclusive quando ele começa ou termina no meio de um mês.
    /// </remarks>
    /// <param name="periodo">Intervalo do pagamento.</param>
    Task<IReadOnlyList<MesDoBalancete>> MovimentoPorMes(PeriodoDoRelatorio periodo, CancellationToken ct = default);

    /// <summary>As despesas do recorte, uma a uma, sem materializar a lista.</summary>
    /// <param name="filtro">Intervalo do vencimento, e o fornecedor, a categoria e a situação quando pedidos.</param>
    IAsyncEnumerable<DespesaExportada> ListarDespesas(FiltroDoRelatorio filtro, CancellationToken ct = default);

    /// <summary>
    /// As parcelas do recorte, uma a uma, sem materializar a lista.
    /// </summary>
    /// <remarks>
    /// A situação <b>não</b> é aplicada aqui: "vencida" não é valor gravado, e quem a resolve é
    /// <c>Parcela.StatusNoDia</c>, na montagem da linha. Ver <see cref="FiltroDoRelatorio"/>.
    /// </remarks>
    /// <param name="filtro">Intervalo do vencimento, e o formando e o item quando pedidos.</param>
    IAsyncEnumerable<ParcelaExportada> ListarParcelas(FiltroDoRelatorio filtro, CancellationToken ct = default);

    /// <summary>
    /// Os nomes por trás dos ids do filtro, para o subtítulo dizer o recorte por extenso.
    /// </summary>
    /// <remarks>Uma consulta por id pedido; sem id nenhum, nenhuma consulta.</remarks>
    /// <param name="filtro">Recorte pedido.</param>
    Task<NomesDoFiltro> NomesDoFiltro(FiltroDoRelatorio filtro, CancellationToken ct = default);

    /// <summary>O que os seletores de filtro da tela oferecem: fornecedores, formandos e itens.</summary>
    Task<OpcoesDeFiltro> OpcoesDeFiltro(CancellationToken ct = default);
}

/// <summary>
/// As solicitações de relatório pesado.
/// </summary>
/// <remarks>
/// Os métodos com sufixo <c>DeTodasAsFormaturas</c> são do worker, que roda sem formatura na sessão —
/// é a saída de emergência documentada do isolamento, e ela mora aqui, não num service.
/// </remarks>
public interface ISolicitacaoDeRelatorioRepository
{
    /// <summary>Marca uma solicitação para inclusão.</summary>
    /// <param name="solicitacao">Pedido.</param>
    Task Adicionar(SolicitacaoDeRelatorio solicitacao, CancellationToken ct = default);

    /// <summary>As solicitações da turma, da mais recente.</summary>
    /// <param name="quantidade">Teto de linhas.</param>
    Task<IReadOnlyList<SolicitacaoDeRelatorio>> Listar(int quantidade, CancellationToken ct = default);

    /// <summary>Uma solicitação da turma, rastreada; nula se não existir aqui.</summary>
    /// <param name="id">Solicitação.</param>
    Task<SolicitacaoDeRelatorio?> Obter(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Se a turma já tem um pedido igual esperando o worker.
    /// </summary>
    /// <remarks>
    /// Clique duplo no botão não pode virar dois PDFs idênticos e dois e-mails.
    /// <para>
    /// "Igual" inclui o recorte inteiro, e não só o tipo e o período: sem isso, quem pede as despesas
    /// de um fornecedor logo depois de alguém ter pedido as de outro recebe o PDF do outro.
    /// </para>
    /// </remarks>
    /// <param name="tipo">O que gerar.</param>
    /// <param name="filtro">Recorte pedido.</param>
    Task<SolicitacaoDeRelatorio?> ObterNaFila(TipoDeRelatorio tipo, FiltroDoRelatorio filtro, CancellationToken ct = default);

    /// <summary>As solicitações na fila, de qualquer turma, da mais antiga.</summary>
    /// <param name="quantidade">Teto de linhas.</param>
    Task<IReadOnlyList<SolicitacaoDeRelatorio>> ListarNaFilaDeTodasAsFormaturas(int quantidade, CancellationToken ct = default);

    /// <summary>As solicitações prontas cujo arquivo já venceu, de qualquer turma.</summary>
    /// <param name="agora">Momento de referência, em UTC.</param>
    /// <param name="quantidade">Teto de linhas.</param>
    Task<IReadOnlyList<SolicitacaoDeRelatorio>> ListarVencidasDeTodasAsFormaturas(DateTime agora, int quantidade, CancellationToken ct = default);
}

/// <summary>Uma solicitação, como a tela a acompanha.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Tipo">O que foi pedido.</param>
/// <param name="De">Primeiro dia do período.</param>
/// <param name="Ate">Último dia do período.</param>
/// <param name="Status">Em que pé está.</param>
/// <param name="Motivo">Por que falhou, quando falhou.</param>
/// <param name="ExpiraEm">Quando o arquivo deixa de estar disponível, em UTC.</param>
/// <param name="CriadoEm">Quando foi pedida, em UTC.</param>
public sealed record SolicitacaoResumo(
    Guid Id,
    TipoDeRelatorio Tipo,
    DateOnly De,
    DateOnly Ate,
    StatusDaSolicitacao Status,
    string? Motivo,
    DateTime? ExpiraEm,
    DateTime CriadoEm
)
{
    /// <summary>
    /// Se o download responde agora — é o que a tela usa para habilitar o botão.
    /// </summary>
    /// <remarks>
    /// Lê o relógio na montagem da resposta, que é o instante em que a pergunta faz sentido. Quem
    /// autoriza de verdade continua sendo <c>IRelatorioService.Baixar</c>, que refaz a conferência: um
    /// arquivo que expira entre a listagem e o clique responde 404, como deve.
    /// </remarks>
    public bool Disponivel => Status == StatusDaSolicitacao.Pronta && ExpiraEm > DateTime.UtcNow;
}
