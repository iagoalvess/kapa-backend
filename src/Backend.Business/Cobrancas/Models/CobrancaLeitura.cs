namespace Backend.Business.Cobrancas.Models;

/// <summary>Nome e regras de atraso do plano, como a tesouraria informa.</summary>
/// <remarks>Percentuais em base 10.000: <c>250</c> é 2,5%. Inteiro, para não arrastar ponto flutuante.</remarks>
/// <param name="Nome">Nome do plano ("Plano 2027").</param>
/// <param name="PercentualDeMulta">Multa por atraso, aplicada uma vez.</param>
/// <param name="PercentualDeJurosAoMes">Juros de mora ao mês.</param>
/// <param name="CarenciaEmDias">Dias depois do vencimento sem multa nem juros.</param>
/// <param name="PercentualDeDescontoPorAntecipacao">Desconto para quem paga antes do vencimento.</param>
/// <param name="DiasMinimosParaDesconto">Dias de antecedência que o desconto exige; zero vale qualquer dia antes do vencimento.</param>
public sealed record DadosDoPlano(
    string Nome,
    int PercentualDeMulta,
    int PercentualDeJurosAoMes,
    int CarenciaEmDias,
    int PercentualDeDescontoPorAntecipacao,
    int DiasMinimosParaDesconto
);

/// <summary>Uma linha do plano, como a tesouraria informa — e como a simulação a recebe.</summary>
/// <param name="Tipo">O que cobra.</param>
/// <param name="Descricao">Nome na tela ("Mensalidade", "Rifa de junho"). Ausente, vale o tipo.</param>
/// <param name="ValorEmCentavos">Valor <b>total</b> por formando, em centavos. É dividido em <paramref name="NumeroDeParcelas"/>.</param>
/// <param name="NumeroDeParcelas">Em quantas vezes.</param>
/// <param name="DiaDeVencimento">De 1 a 31; no mês mais curto, vale o último dia.</param>
/// <param name="PrimeiroMes">Mês do primeiro vencimento; o dia é ignorado.</param>
public sealed record DadosDoItem(
    TipoDeCobranca Tipo,
    string? Descricao,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    int DiaDeVencimento,
    DateOnly PrimeiroMes
)
{
    /// <summary>A grade deste item.</summary>
    public IReadOnlyList<ParcelaPrevista> Grade() => GradeDeParcelas.Calcular(this);
}

/// <summary>
/// A marca que faz um item novo alcançar também quem já aderiu — o rateio extraordinário.
/// </summary>
/// <remarks>
/// Ausente, vale a regra normal: a parcela nasce na adesão, e o item novo só alcança quem aderir
/// depois. Presente, a turma decidiu por todos, e a origem dessa decisão fica gravada no item.
/// <para>
/// É um tipo próprio, e não um par de campos em <see cref="DadosDoItem"/>, por dois motivos:
/// <see cref="DadosDoItem"/> é congelado no snapshot da adesão e não deve carregar comando, e
/// cobrar a turma inteira precisa ser um argumento que alguém escreveu — não um <c>bool</c> que
/// vai junto por engano.
/// </para>
/// </remarks>
/// <param name="OrigemDaDecisao">Onde a turma decidiu: "assembleia de 12/10".</param>
public sealed record RateioExtraordinario(string OrigemDaDecisao);

/// <summary>Plano na lista da turma.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome do plano.</param>
/// <param name="Status">Em montagem ou vigente.</param>
/// <param name="VigenteDesde">Quando entrou em vigor, em UTC.</param>
public sealed record PlanoDeCobrancaResumo(Guid Id, string Nome, StatusDoPlano Status, DateTime? VigenteDesde);

/// <summary>O plano inteiro, com os itens.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome do plano.</param>
/// <param name="Status">Em montagem ou vigente.</param>
/// <param name="VigenteDesde">Quando entrou em vigor, em UTC.</param>
/// <param name="PercentualDeMulta">Multa, base 10.000.</param>
/// <param name="PercentualDeJurosAoMes">Juros ao mês, base 10.000.</param>
/// <param name="CarenciaEmDias">Dias sem multa nem juros.</param>
/// <param name="PercentualDeDescontoPorAntecipacao">Desconto por antecipação, base 10.000.</param>
/// <param name="DiasMinimosParaDesconto">Dias de antecedência que o desconto exige.</param>
/// <param name="Itens">Itens, na ordem em que foram criados.</param>
/// <param name="FormandosComParcela">
/// Quantos já têm parcela deste plano — quem aderiu. Item incluído agora não os alcança (decisão de
/// 14/09/2026), salvo no rateio extraordinário: a tela avisa com este número.
/// </param>
public sealed record PlanoDeCobrancaDetalhe(
    Guid Id,
    string Nome,
    StatusDoPlano Status,
    DateTime? VigenteDesde,
    int PercentualDeMulta,
    int PercentualDeJurosAoMes,
    int CarenciaEmDias,
    int PercentualDeDescontoPorAntecipacao,
    int DiasMinimosParaDesconto,
    IReadOnlyList<ItemDeCobrancaDetalhe> Itens,
    int FormandosComParcela
);

/// <summary>Um item do plano.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Tipo">O que cobra.</param>
/// <param name="Descricao">Nome na tela, se houver.</param>
/// <param name="ValorEmCentavos">Valor total por formando, em centavos.</param>
/// <param name="NumeroDeParcelas">Em quantas vezes.</param>
/// <param name="DiaDeVencimento">Dia do vencimento.</param>
/// <param name="PrimeiroMes">Mês do primeiro vencimento, no dia 1.</param>
/// <param name="EncerradoEm">Quando deixou de cobrar, se deixou.</param>
/// <param name="EmUso">Já gerou parcela: não pode ser removido, só encerrado, e só o valor muda.</param>
/// <param name="OrigemDaDecisao">Onde a turma decidiu, se o item foi um rateio extraordinário.</param>
public sealed record ItemDeCobrancaDetalhe(
    Guid Id,
    TipoDeCobranca Tipo,
    string? Descricao,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    int DiaDeVencimento,
    DateOnly PrimeiroMes,
    DateOnly? EncerradoEm,
    bool EmUso,
    string? OrigemDaDecisao
);

/// <summary>Pedido de simulação.</summary>
/// <param name="Itens">Itens a simular — os do formulário, ainda não gravados. Ausente, simula os itens gravados.</param>
public sealed record SimularPlano(IReadOnlyList<DadosDoItem>? Itens);

/// <summary>Uma parcela da simulação.</summary>
/// <param name="Tipo">Tipo do item de origem.</param>
/// <param name="Descricao">Descrição do item de origem, se houver.</param>
/// <param name="Numero">Posição na grade do item.</param>
/// <param name="De">Total de parcelas do item — o "24" de "1/24".</param>
/// <param name="Vencimento">Dia do vencimento.</param>
/// <param name="ValorEmCentavos">Valor, em centavos.</param>
public sealed record ParcelaSimulada(TipoDeCobranca Tipo, string? Descricao, int Numero, int De, DateOnly Vencimento, long ValorEmCentavos);

/// <summary>A grade de um formando e o que ela significa para a turma.</summary>
/// <param name="Parcelas">Todas as parcelas de um formando, por vencimento.</param>
/// <param name="TotalPorFormando">Soma das parcelas, em centavos.</param>
/// <param name="Formandos">Membros ativos da turma hoje — quem o plano alcança se todos aderirem.</param>
/// <param name="TotalDaTurma">Total por formando vezes os formandos, em centavos.</param>
public sealed record SimulacaoDoPlano(IReadOnlyList<ParcelaSimulada> Parcelas, long TotalPorFormando, int Formandos, long TotalDaTurma);

/// <summary>Filtros da lista de parcelas.</summary>
/// <param name="UsuarioId">Só as deste formando.</param>
/// <param name="Status">Só nesta situação. <see cref="StatusDaParcela.Vencida"/> e <see cref="StatusDaParcela.Aberta"/> dividem as abertas pelo dia de hoje.</param>
/// <param name="De">Vencimento a partir deste dia, inclusive.</param>
/// <param name="Ate">Vencimento até este dia, inclusive.</param>
/// <param name="Busca">Trecho do nome da conta ou do nome civil — é como a tesouraria acha quem pagou e não avisou.</param>
public sealed record FiltroDeParcelas(
    Guid? UsuarioId = null,
    StatusDaParcela? Status = null,
    DateOnly? De = null,
    DateOnly? Ate = null,
    string? Busca = null
);

/// <summary>Uma parcela, como a gestão e o próprio formando a veem.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="VinculoId">Vínculo de quem deve — é o que liga a parcela às regras aceitas na adesão.</param>
/// <param name="UsuarioId">Formando que deve.</param>
/// <param name="Nome">Nome civil, se informado no cadastro; senão, o da conta.</param>
/// <param name="Tipo">Tipo do item de origem.</param>
/// <param name="Descricao">Descrição do item de origem, se houver.</param>
/// <param name="Numero">Posição na grade do item.</param>
/// <param name="De">Total de parcelas do item.</param>
/// <param name="Vencimento">Dia do vencimento.</param>
/// <param name="ValorOriginalEmCentavos">Valor antes de multa e juros, em centavos.</param>
/// <param name="Status">Situação no dia de hoje.</param>
/// <param name="EmConferencia">Tem aviso de pagamento esperando a tesouraria. Leitura, não status.</param>
/// <param name="ValorPagoEmCentavos">O que entrou, se paga.</param>
/// <param name="PagoEm">Dia em que entrou, se paga.</param>
/// <param name="ValorDoDia">O valor de hoje, com encargos ou desconto — só na aberta e na vencida.</param>
public sealed record ParcelaResumo(
    Guid Id,
    Guid VinculoId,
    Guid UsuarioId,
    string Nome,
    TipoDeCobranca Tipo,
    string? Descricao,
    int Numero,
    int De,
    DateOnly Vencimento,
    long ValorOriginalEmCentavos,
    StatusDaParcela Status,
    bool EmConferencia = false,
    long? ValorPagoEmCentavos = null,
    DateOnly? PagoEm = null,
    ValorDoDia? ValorDoDia = null
)
{
    /// <summary>Se ainda se deve: aberta ou vencida.</summary>
    public bool EmAberto => Status is StatusDaParcela.Aberta or StatusDaParcela.Vencida;

    /// <summary>A mesma parcela com o valor do dia calculado — nulo se não está em aberto.</summary>
    /// <param name="dia">Dia de referência.</param>
    /// <param name="regras">Regras aceitas na adesão.</param>
    public ParcelaResumo ComValorDoDia(DateOnly dia, RegrasDeAtraso regras) =>
        this with
        {
            ValorDoDia = EmAberto ? ValorDoDia.Calcular(ValorOriginalEmCentavos, Vencimento, dia, regras) : null,
        };
}

/// <summary>Quantas parcelas há numa situação e quanto somam.</summary>
/// <param name="Quantidade">Parcelas.</param>
/// <param name="ValorEmCentavos">Soma: o original, ou o que entrou nas pagas.</param>
public sealed record SomaDeParcelas(int Quantidade, long ValorEmCentavos)
{
    /// <summary>Nenhuma parcela.</summary>
    public static readonly SomaDeParcelas Zero = new(0, 0);
}

/// <summary>Uma linha da contagem agrupada, como sai do banco.</summary>
/// <param name="Status">Situação no dia, com a vencida já separada.</param>
/// <param name="Quantidade">Parcelas.</param>
/// <param name="OriginalEmCentavos">Soma do valor original.</param>
/// <param name="PagoEmCentavos">Soma do que entrou.</param>
public sealed record ContagemDeParcelas(StatusDaParcela Status, int Quantidade, long OriginalEmCentavos, long PagoEmCentavos);

/// <summary>Uma parcela vencida, com o que o valor do dia precisa.</summary>
/// <param name="VinculoId">Vínculo, para as regras aceitas.</param>
/// <param name="ValorOriginalEmCentavos">Valor antes dos encargos.</param>
/// <param name="Vencimento">Dia do vencimento.</param>
public sealed record ParcelaEmAtraso(Guid VinculoId, long ValorOriginalEmCentavos, DateOnly Vencimento);

/// <summary>A faixa da tela Parcelas em dinheiro: quantas e quanto, por situação.</summary>
/// <param name="Todas">Todas as parcelas do filtro, pelo valor original.</param>
/// <param name="Aberta">A vencer.</param>
/// <param name="Vencida">Vencidas, pelo valor original.</param>
/// <param name="Paga">Pagas, pelo que entrou — o recebido no período.</param>
/// <param name="Cancelada">Canceladas.</param>
/// <param name="VencidoAtualizadoEmCentavos">As vencidas pelo valor de hoje, com multa e juros.</param>
public sealed record ResumoDeParcelas(
    SomaDeParcelas Todas,
    SomaDeParcelas Aberta,
    SomaDeParcelas Vencida,
    SomaDeParcelas Paga,
    SomaDeParcelas Cancelada,
    long VencidoAtualizadoEmCentavos
);
