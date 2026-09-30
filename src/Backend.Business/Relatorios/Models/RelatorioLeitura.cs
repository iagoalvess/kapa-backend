using Backend.Business.Cobrancas.Models;
using Backend.Business.Financeiro.Models;

namespace Backend.Business.Relatorios.Models;

/// <summary>
/// O intervalo que o relatório cobre.
/// </summary>
/// <remarks>
/// Só o relatório tem período (decisão 9): saldo em caixa é estado de hoje, e "saldo entre março e
/// junho" não quer dizer nada. Aqui a pergunta é outra — o que entrou e o que saiu entre dois dias.
/// <para>
/// As duas pontas são inclusivas e sempre preenchidas: <see cref="Normalizar"/> completa o que a
/// borda não mandou, e daí para dentro ninguém trata nulo.
/// </para>
/// </remarks>
/// <param name="De">Primeiro dia incluído.</param>
/// <param name="Ate">Último dia incluído.</param>
public sealed record PeriodoDoRelatorio(DateOnly De, DateOnly Ate)
{
    /// <summary>Teto do intervalo, em dias. Cinco anos cobrem a turma mais longa que existe.</summary>
    public const int MaximoDeDias = 1826;

    /// <summary>
    /// O período pedido, com as pontas que faltam completadas e a ordem corrigida.
    /// </summary>
    /// <remarks>
    /// Sem nada, é o ano corrente até hoje — o recorte que o tesoureiro quer em nove de cada dez
    /// aberturas. Data invertida é trocada em vez de recusada: o usuário quis o intervalo entre as
    /// duas, e um 400 aqui só o faria digitar de novo.
    /// </remarks>
    /// <param name="de">Começo pedido.</param>
    /// <param name="ate">Fim pedido.</param>
    /// <param name="hoje">Dia de referência.</param>
    public static PeriodoDoRelatorio Normalizar(DateOnly? de, DateOnly? ate, DateOnly hoje)
    {
        var fim = ate ?? hoje;
        var inicio = de ?? new DateOnly(fim.Year, 1, 1);

        if (inicio > fim)
            (inicio, fim) = (fim, inicio);

        return new PeriodoDoRelatorio(Maior(inicio, fim.AddDays(-MaximoDeDias)), fim);
    }

    /// <summary>Dias cobertos, as duas pontas inclusive.</summary>
    public int Dias => Ate.DayNumber - De.DayNumber + 1;

    /// <summary>
    /// O período imediatamente anterior, do mesmo tamanho — a base da comparação da tela.
    /// </summary>
    /// <remarks>
    /// Termina na véspera deste, e não no mesmo dia do mês passado: comparar 30 dias com 31 faria a
    /// variação subir sozinha só porque o mês é mais longo.
    /// </remarks>
    public PeriodoDoRelatorio Anterior()
    {
        var fim = De.AddDays(-1);

        return new PeriodoDoRelatorio(fim.AddDays(1 - Dias), fim);
    }

    private static DateOnly Maior(DateOnly a, DateOnly b) => a > b ? a : b;
}

/// <summary>
/// O recorte de um relatório: o período mais o que estreita as linhas.
/// </summary>
/// <remarks>
/// Um registro só para os quatro relatórios, com cada um usando os campos que lhe dizem respeito —
/// é a mesma escolha do <see cref="TabelaDoRelatorio"/>, e pelo mesmo motivo: um filtro por relatório
/// seriam quatro tipos, quatro assinaturas de service, quatro contratos e quatro telas.
/// <para>
/// <b>O balancete só aceita período.</b> Ele é o documento da assembleia: recortado por um fornecedor
/// as entradas deixam de bater com as saídas, e o que sai é um papel que não fecha com a cara do que
/// fecha. <see cref="SomentePeriodo"/> é o que o service aplica nele.
/// </para>
/// <para>
/// A situação da parcela é o único campo que <b>não</b> vira <c>where</c> no banco: "vencida" não é
/// um valor gravado, é uma aberta cujo vencimento passou, e essa regra mora em
/// <c>Parcela.StatusNoDia</c>. O relatório já a chama ao montar a linha, e é lá que o filtro entra —
/// em memória, sobre a linha já resolvida. Duplicá-la em SQL seria a segunda cópia da regra.
/// </para>
/// </remarks>
/// <param name="Periodo">Intervalo pedido, já normalizado. Sempre presente.</param>
/// <param name="FornecedorId">Despesas: só as deste fornecedor.</param>
/// <param name="Categoria">Despesas: só as desta categoria.</param>
/// <param name="SituacaoDaDespesa">Despesas: só nesta situação.</param>
/// <param name="FormandoId">Parcelas: só as deste formando.</param>
/// <param name="ItemDeCobrancaId">Parcelas: só as originadas deste item.</param>
/// <param name="SituacaoDaParcela">Parcelas: só nesta situação, resolvida pelo dia de hoje.</param>
public sealed record FiltroDoRelatorio(
    PeriodoDoRelatorio Periodo,
    Guid? FornecedorId = null,
    CategoriaDeDespesa? Categoria = null,
    StatusDaDespesa? SituacaoDaDespesa = null,
    Guid? FormandoId = null,
    Guid? ItemDeCobrancaId = null,
    StatusDaParcela? SituacaoDaParcela = null
)
{
    /// <summary>O mesmo período, sem recorte nenhum — o que o balancete aceita.</summary>
    public FiltroDoRelatorio SomentePeriodo() => new(Periodo);

    /// <summary>Se algum recorte além do período foi pedido.</summary>
    public bool TemRecorte =>
        FornecedorId is not null
        || Categoria is not null
        || SituacaoDaDespesa is not null
        || FormandoId is not null
        || ItemDeCobrancaId is not null
        || SituacaoDaParcela is not null;
}

/// <summary>
/// Os nomes por trás dos ids do filtro.
/// </summary>
/// <remarks>
/// O subtítulo do relatório precisa dizer "Fornecedor: Buffet Sabor", e não um GUID: um arquivo que
/// mostra parte das linhas sem dizer qual parte é um arquivo que mente. Vazio quando o id não foi
/// pedido — ou quando aponta para algo que não existe mais na turma.
/// </remarks>
/// <param name="Fornecedor">Nome do fornecedor escolhido.</param>
/// <param name="Formando">Nome do formando escolhido.</param>
/// <param name="Item">Rótulo do item de cobrança escolhido.</param>
public readonly record struct NomesDoFiltro(string? Fornecedor, string? Formando, string? Item);

/// <summary>Uma opção de um seletor de filtro: o que gravar e o que mostrar.</summary>
/// <param name="Id">Valor que vai no filtro.</param>
/// <param name="Nome">Como a tela o escreve.</param>
public sealed record OpcaoDeFiltro(Guid Id, string Nome);

/// <summary>
/// O que os seletores de filtro da tela de relatórios oferecem.
/// </summary>
/// <remarks>
/// Endpoint próprio, e não as listas de Fornecedores, Membros e Itens: o seletor quer id e nome, sem
/// paginação, sem contagem e sem o resto do cadastro — e são três consultas numa requisição, em vez
/// de três requisições a três contratos que mudam por motivos que não são deste filtro.
/// <para>
/// Os formandos saem de quem <b>tem parcela</b>: filtrar por alguém sem nenhuma devolveria um
/// relatório vazio, e o nome vem da mesma regra da tela de Parcelas.
/// </para>
/// </remarks>
/// <param name="Fornecedores">Fornecedores da turma, em ordem alfabética.</param>
/// <param name="Formandos">Quem tem parcela, em ordem alfabética.</param>
/// <param name="Itens">Itens de cobrança da turma.</param>
public sealed record OpcoesDeFiltro(
    IReadOnlyList<OpcaoDeFiltro> Fornecedores,
    IReadOnlyList<OpcaoDeFiltro> Formandos,
    IReadOnlyList<OpcaoDeFiltro> Itens
);

/// <summary>
/// O que o formando lê: quanto da turma está em dia e com quem ela gastou.
/// </summary>
/// <remarks>
/// <b>Nenhum campo aqui aponta para uma pessoa.</b> É a regra jurídica da sprint: vergonha pública
/// por dívida é ilícita. Nada de acrescentar aqui um campo de gestão e filtrá-lo no cliente — se a
/// lista de quem deve voltar a fazer falta, volta como consulta própria, atrás de política própria.
/// <para>
/// O caixa em si não entra aqui: é do <c>ICaixaService</c> (decisão 4), e a tela o lê de lá.
/// </para>
/// </remarks>
/// <param name="Adimplencia">Quanto do que já venceu entrou.</param>
/// <param name="PorFornecedor">O que saiu para cada fornecedor, do maior para o menor.</param>
public sealed record IndicadoresPublicos(Adimplencia Adimplencia, IReadOnlyList<GastoPorFornecedor> PorFornecedor);

/// <summary>
/// Quanto do que já venceu entrou.
/// </summary>
/// <remarks>
/// Percentual de dinheiro, não de gente (decisão 6): recebido sobre devido até hoje. Base 10.000,
/// como todo percentual do contrato — <c>9.850</c> é 98,5%.
/// <para>
/// O denominador é só o que <b>já venceu</b>. Contar a parcela de dezembro como inadimplência em
/// março faria toda turma nova parecer quebrada no primeiro mês.
/// </para>
/// </remarks>
/// <param name="DevidoEmCentavos">O que venceu até hoje, pelo valor original.</param>
/// <param name="RecebidoEmCentavos">O que entrou do que venceu.</param>
public sealed record Adimplencia(long DevidoEmCentavos, long RecebidoEmCentavos)
{
    /// <summary>O que falta entrar do que já venceu.</summary>
    public long EmAtrasoEmCentavos => DevidoEmCentavos - RecebidoEmCentavos;

    /// <summary>O índice em base 10.000. Sem nada vencido, 100%.</summary>
    public int PercentualBaseDezMil =>
        DevidoEmCentavos <= 0 ? 10_000 : (int)Math.Clamp(Math.Round(RecebidoEmCentavos * 10_000m / DevidoEmCentavos), 0, 10_000);
}

/// <summary>Quanto a turma pagou a um fornecedor.</summary>
/// <param name="FornecedorId">Fornecedor; nulo agrupa os gastos sem fornecedor cadastrado.</param>
/// <param name="Nome">Nome do fornecedor, ou "Sem fornecedor".</param>
/// <param name="Quantidade">Despesas lançadas com ele, canceladas de fora.</param>
/// <param name="PagoEmCentavos">O que já saiu.</param>
/// <param name="PrevistoEmCentavos">O que ainda vai sair.</param>
public sealed record GastoPorFornecedor(Guid? FornecedorId, string Nome, int Quantidade, long PagoEmCentavos, long PrevistoEmCentavos);

/// <summary>
/// O documento da assembleia: o que entrou, o que saiu e o que sobrou, num período.
/// </summary>
/// <remarks>
/// Não é dump de tabela (decisão 3). Tem capa, resumo, entradas por categoria, saídas por categoria
/// e por fornecedor, saldo e quem emitiu — é o que a comissão projeta na parede.
/// <para>
/// O <see cref="SaldoDoPeriodoEmCentavos"/> é do período; o <see cref="SaldoAcumuladoEmCentavos"/> é
/// o de hoje, o mesmo do dashboard, e vem do <c>ICaixaService</c> (decisão 4) — é o que faz as duas
/// telas nunca discordarem.
/// </para>
/// </remarks>
/// <param name="Formatura">Nome da turma, para a capa.</param>
/// <param name="Instituicao">Instituição e curso, para a capa.</param>
/// <param name="Periodo">Intervalo coberto.</param>
/// <param name="EmitidoPor">Nome de quem pediu o relatório.</param>
/// <param name="EmitidoEm">Momento da emissão, em UTC.</param>
/// <param name="Entradas">Recebimentos do período, por tipo de cobrança.</param>
/// <param name="OutrasReceitas">Receitas que não vêm de formando recebidas no período, por categoria (Sprint 28).</param>
/// <param name="SaidasPorCategoria">Despesas pagas no período, por categoria.</param>
/// <param name="SaidasPorFornecedor">Despesas pagas no período, por fornecedor.</param>
/// <param name="SaldoAcumuladoEmCentavos">O saldo da turma hoje — arrecadado menos gasto, desde sempre.</param>
/// <param name="Meses">O movimento mês a mês dentro do período, sem buraco — a curva da tela.</param>
/// <param name="Anterior">Os mesmos totais no período anterior, de igual tamanho — a base da variação.</param>
public sealed record Balancete(
    string Formatura,
    string Instituicao,
    PeriodoDoRelatorio Periodo,
    string EmitidoPor,
    DateTime EmitidoEm,
    IReadOnlyList<LinhaDeBalancete> Entradas,
    IReadOnlyList<LinhaDeBalancete> OutrasReceitas,
    IReadOnlyList<LinhaDeBalancete> SaidasPorCategoria,
    IReadOnlyList<LinhaDeBalancete> SaidasPorFornecedor,
    long SaldoAcumuladoEmCentavos,
    IReadOnlyList<MesDoBalancete> Meses,
    TotaisDoPeriodo Anterior
)
{
    /// <summary>O que entrou de parcela no período.</summary>
    public long ParcelasEmCentavos => Entradas.Sum(linha => linha.ValorEmCentavos);

    /// <summary>O que entrou de receita no período.</summary>
    public long OutrasReceitasEmCentavos => OutrasReceitas.Sum(linha => linha.ValorEmCentavos);

    /// <summary>O que entrou no período: parcelas e receitas — a soma que fecha com o caixa.</summary>
    public long EntradasEmCentavos => ParcelasEmCentavos + OutrasReceitasEmCentavos;

    /// <summary>O que saiu no período.</summary>
    public long SaidasEmCentavos => SaidasPorCategoria.Sum(linha => linha.ValorEmCentavos);

    /// <summary>Entradas menos saídas do período. Pode ser negativo.</summary>
    public long SaldoDoPeriodoEmCentavos => EntradasEmCentavos - SaidasEmCentavos;
}

/// <summary>Um mês do período, no gráfico do balancete.</summary>
/// <param name="Mes">Primeiro dia do mês.</param>
/// <param name="EntradasEmCentavos">O que entrou no caixa nele, dentro do período.</param>
/// <param name="SaidasEmCentavos">O que saiu nele, dentro do período.</param>
public sealed record MesDoBalancete(DateOnly Mes, long EntradasEmCentavos, long SaidasEmCentavos);

/// <summary>Entradas e saídas de um período, sem abrir por categoria.</summary>
/// <remarks>É o que a tela precisa do período anterior: só os totais, para calcular a variação.</remarks>
/// <param name="EntradasEmCentavos">O que entrou.</param>
/// <param name="SaidasEmCentavos">O que saiu.</param>
public sealed record TotaisDoPeriodo(long EntradasEmCentavos, long SaidasEmCentavos)
{
    /// <summary>Entradas menos saídas. Pode ser negativo.</summary>
    public long ResultadoEmCentavos => EntradasEmCentavos - SaidasEmCentavos;
}

/// <summary>Uma linha agrupada do balancete.</summary>
/// <param name="Rotulo">Categoria, tipo de cobrança ou nome do fornecedor.</param>
/// <param name="Quantidade">Lançamentos somados.</param>
/// <param name="ValorEmCentavos">Soma deles.</param>
public sealed record LinhaDeBalancete(string Rotulo, int Quantidade, long ValorEmCentavos);

/// <summary>Uma linha do CSV de despesas.</summary>
/// <param name="Vencimento">Dia do pagamento previsto.</param>
/// <param name="Competencia">Mês em que o gasto aconteceu.</param>
/// <param name="Descricao">O que é.</param>
/// <param name="Categoria">Em que a turma gastou.</param>
/// <param name="Fornecedor">A quem se paga; vazio quando não há fornecedor cadastrado.</param>
/// <param name="Status">Situação da linha.</param>
/// <param name="ValorEmCentavos">Valor desta linha.</param>
/// <param name="PagoEm">Dia em que o dinheiro saiu, se saiu.</param>
public sealed record DespesaExportada(
    DateOnly Vencimento,
    DateOnly Competencia,
    string Descricao,
    CategoriaDeDespesa Categoria,
    string Fornecedor,
    StatusDaDespesa Status,
    long ValorEmCentavos,
    DateOnly? PagoEm
);

/// <summary>
/// Uma linha do CSV de parcelas.
/// </summary>
/// <remarks>Nomeia quem deve: é exportação de gestão, como a tela de Parcelas.</remarks>
/// <param name="Nome">Formando.</param>
/// <param name="Item">Item de cobrança de origem.</param>
/// <param name="Numero">Posição na grade.</param>
/// <param name="De">Total de parcelas do item.</param>
/// <param name="Vencimento">Dia do vencimento.</param>
/// <param name="ValorOriginalEmCentavos">Valor antes de multa e juros.</param>
/// <param name="Status">Situação gravada — a vencida é resolvida na exportação, pelo dia.</param>
/// <param name="ValorPagoEmCentavos">O que entrou, se paga.</param>
/// <param name="PagoEm">Dia em que entrou, se paga.</param>
public sealed record ParcelaExportada(
    string Nome,
    string Item,
    int Numero,
    int De,
    DateOnly Vencimento,
    long ValorOriginalEmCentavos,
    StatusDaParcela Status,
    long? ValorPagoEmCentavos,
    DateOnly? PagoEm
);
