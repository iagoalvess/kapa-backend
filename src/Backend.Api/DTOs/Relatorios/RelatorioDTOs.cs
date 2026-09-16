using Backend.Api.DTOs.Financeiro;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Financeiro.Models;
using Backend.Business.Relatorios.Models;

namespace Backend.Api.DTOs.Relatorios;

/// <summary>
/// O painel do formando: quanto a turma tem, no que gastou e quanto dela está em dia.
/// </summary>
/// <remarks>
/// <b>Nenhum campo deste contrato identifica uma pessoa.</b> É soma, e só soma — quem deve e quanto
/// se vê na tela de Parcelas, que tem política própria.
/// </remarks>
/// <param name="Caixa">Arrecadado, gasto, saldo, a receber e o quadro por categoria.</param>
/// <param name="Adimplencia">Quanto do que já venceu entrou.</param>
/// <param name="PorFornecedor">O que saiu para cada fornecedor, do maior para o menor.</param>
/// <param name="Meses">O caixa mês a mês, com a projeção até a colação.</param>
public sealed record DashboardPublicoDTO(
    CaixaDTO Caixa,
    AdimplenciaDTO Adimplencia,
    IReadOnlyList<GastoPorFornecedorDTO> PorFornecedor,
    IReadOnlyList<MesDoCaixaDTO> Meses
);

/// <summary>Quanto do que já venceu entrou.</summary>
/// <param name="DevidoEmCentavos">O que venceu até hoje, pelo valor original.</param>
/// <param name="RecebidoEmCentavos">O que entrou do que venceu.</param>
/// <param name="EmAtrasoEmCentavos">A diferença entre os dois.</param>
/// <param name="PercentualBaseDezMil">O índice em base 10.000 — <c>9850</c> é 98,5%. Sem nada vencido, 10.000.</param>
public sealed record AdimplenciaDTO(long DevidoEmCentavos, long RecebidoEmCentavos, long EmAtrasoEmCentavos, int PercentualBaseDezMil);

/// <summary>Quanto a turma pagou a um fornecedor.</summary>
/// <param name="FornecedorId">Fornecedor; nulo agrupa os gastos sem fornecedor cadastrado.</param>
/// <param name="Nome">Nome do fornecedor, ou "Sem fornecedor".</param>
/// <param name="Quantidade">Despesas lançadas com ele.</param>
/// <param name="PagoEmCentavos">O que já saiu.</param>
/// <param name="PrevistoEmCentavos">O que ainda vai sair.</param>
public sealed record GastoPorFornecedorDTO(Guid? FornecedorId, string Nome, int Quantidade, long PagoEmCentavos, long PrevistoEmCentavos);

/// <summary>O balancete do período, consolidado.</summary>
/// <param name="Formatura">Nome da turma.</param>
/// <param name="Instituicao">Curso e instituição.</param>
/// <param name="De">Primeiro dia do período.</param>
/// <param name="Ate">Último dia do período.</param>
/// <param name="EmitidoPor">Quem pediu.</param>
/// <param name="EmitidoEm">Momento da emissão, em UTC.</param>
/// <param name="Entradas">Recebimentos do período, por tipo de cobrança.</param>
/// <param name="SaidasPorCategoria">Despesas pagas no período, por categoria.</param>
/// <param name="SaidasPorFornecedor">Despesas pagas no período, por fornecedor.</param>
/// <param name="EntradasEmCentavos">O que entrou no período.</param>
/// <param name="SaidasEmCentavos">O que saiu no período.</param>
/// <param name="SaldoDoPeriodoEmCentavos">Entradas menos saídas. Pode ser negativo.</param>
/// <param name="SaldoAcumuladoEmCentavos">O saldo da turma hoje — o mesmo do dashboard.</param>
public sealed record BalanceteDTO(
    string Formatura,
    string Instituicao,
    DateOnly De,
    DateOnly Ate,
    string EmitidoPor,
    DateTime EmitidoEm,
    IReadOnlyList<LinhaDeBalanceteDTO> Entradas,
    IReadOnlyList<LinhaDeBalanceteDTO> SaidasPorCategoria,
    IReadOnlyList<LinhaDeBalanceteDTO> SaidasPorFornecedor,
    long EntradasEmCentavos,
    long SaidasEmCentavos,
    long SaldoDoPeriodoEmCentavos,
    long SaldoAcumuladoEmCentavos,
    IReadOnlyList<MesDoBalanceteDTO> Meses,
    TotaisDoPeriodoDTO Anterior
);

/// <summary>Um mês do período, no gráfico do balancete.</summary>
/// <param name="Mes">Primeiro dia do mês.</param>
/// <param name="EntradasEmCentavos">O que entrou no caixa nele, dentro do período.</param>
/// <param name="SaidasEmCentavos">O que saiu nele, dentro do período.</param>
public sealed record MesDoBalanceteDTO(DateOnly Mes, long EntradasEmCentavos, long SaidasEmCentavos);

/// <summary>Os totais do período anterior, de igual tamanho — a base da variação da tela.</summary>
/// <param name="EntradasEmCentavos">O que entrou.</param>
/// <param name="SaidasEmCentavos">O que saiu.</param>
/// <param name="ResultadoEmCentavos">Entradas menos saídas. Pode ser negativo.</param>
public sealed record TotaisDoPeriodoDTO(long EntradasEmCentavos, long SaidasEmCentavos, long ResultadoEmCentavos);

/// <summary>Uma linha agrupada do balancete.</summary>
/// <param name="Rotulo">Categoria, tipo de cobrança ou nome do fornecedor.</param>
/// <param name="Quantidade">Lançamentos somados.</param>
/// <param name="ValorEmCentavos">Soma deles.</param>
public sealed record LinhaDeBalanceteDTO(string Rotulo, int Quantidade, long ValorEmCentavos);

/// <summary>
/// O recorte de um relatório: o período e o que estreita as linhas.
/// </summary>
/// <remarks>
/// Os mesmos campos nos dois formatos — query string na planilha, corpo na solicitação do PDF —, para
/// eles não divergirem. Por isso é um registro de propriedades, e não posicional: a solicitação
/// <b>herda</b> daqui e acrescenta só o tipo, em vez de repetir os oito campos.
/// <para>
/// Campo de recorte que não é do relatório pedido é ignorado, e o balancete ignora todos: filtrado,
/// ele deixa de fechar. Quem decide isso é o service, não a borda.
/// </para>
/// </remarks>
public record RecorteDoRelatorioDTO
{
    /// <summary>Primeiro dia incluído. Ausente, o começo do ano do fim.</summary>
    public DateOnly? De { get; init; }

    /// <summary>Último dia incluído. Ausente, hoje.</summary>
    public DateOnly? Ate { get; init; }

    /// <summary>Despesas e fornecedores: só os deste fornecedor.</summary>
    public Guid? FornecedorId { get; init; }

    /// <summary>Despesas: só as desta categoria.</summary>
    public CategoriaDeDespesa? Categoria { get; init; }

    /// <summary>Despesas: só as nesta situação.</summary>
    public StatusDaDespesa? SituacaoDaDespesa { get; init; }

    /// <summary>Parcelas: só as deste formando.</summary>
    public Guid? FormandoId { get; init; }

    /// <summary>Parcelas: só as deste item de cobrança.</summary>
    public Guid? ItemDeCobrancaId { get; init; }

    /// <summary>Parcelas: só as nesta situação, resolvida pelo dia de hoje.</summary>
    public StatusDaParcela? SituacaoDaParcela { get; init; }

    /// <summary>O recorte como o Business o lê, com as pontas do período completadas.</summary>
    /// <param name="hoje">Dia de referência para o período padrão.</param>
    public FiltroDoRelatorio ParaFiltro(DateOnly hoje) =>
        new(
            PeriodoDoRelatorio.Normalizar(De, Ate, hoje),
            FornecedorId,
            Categoria,
            SituacaoDaDespesa,
            FormandoId,
            ItemDeCobrancaId,
            SituacaoDaParcela
        );
}

/// <summary>Uma opção de um seletor de filtro.</summary>
/// <param name="Id">Valor que vai no filtro.</param>
/// <param name="Nome">Como a tela o escreve.</param>
public sealed record OpcaoDeFiltroDTO(Guid Id, string Nome);

/// <summary>O que os seletores de filtro da tela de relatórios oferecem.</summary>
/// <param name="Fornecedores">Fornecedores da turma, em ordem alfabética.</param>
/// <param name="Formandos">Quem tem parcela, em ordem alfabética.</param>
/// <param name="Itens">Itens de cobrança da turma.</param>
public sealed record OpcoesDeFiltroDTO(
    IReadOnlyList<OpcaoDeFiltroDTO> Fornecedores,
    IReadOnlyList<OpcaoDeFiltroDTO> Formandos,
    IReadOnlyList<OpcaoDeFiltroDTO> Itens
);

/// <summary>O relatório e o recorte pedidos no corpo da solicitação de PDF.</summary>
public sealed record SolicitarRelatorioDTO : RecorteDoRelatorioDTO
{
    /// <summary>Qual relatório. Ausente, o balancete.</summary>
    public TipoDeRelatorio? Tipo { get; init; }
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
/// <param name="Disponivel">Se o download responde agora — é o que a tela usa para habilitar o botão.</param>
public sealed record SolicitacaoDTO(
    Guid Id,
    TipoDeRelatorio Tipo,
    DateOnly De,
    DateOnly Ate,
    StatusDaSolicitacao Status,
    string? Motivo,
    DateTime? ExpiraEm,
    DateTime CriadoEm,
    bool Disponivel
);
