using Backend.Business.Cobrancas.Models;

namespace Backend.Api.DTOs.Cobrancas;

/// <summary>Corpo de criação e alteração do plano.</summary>
/// <param name="Nome">Nome do plano.</param>
/// <param name="PercentualDeMulta">Multa por atraso, base 10.000 — <c>200</c> é 2%.</param>
/// <param name="PercentualDeJurosAoMes">Juros de mora ao mês, base 10.000 — <c>100</c> é 1%.</param>
/// <param name="CarenciaEmDias">Dias depois do vencimento sem multa nem juros.</param>
/// <param name="PercentualDeDescontoPorAntecipacao">Desconto por pagamento antecipado, base 10.000.</param>
/// <param name="DiasMinimosParaDesconto">
/// Dias de antecedência que o desconto exige. Obrigatório acima de zero quando há desconto (400
/// <c>cobranca.antecedencia_obrigatoria</c>): sem ele, quem paga um dia antes leva o desconto inteiro.
/// </param>
public sealed record PlanoDeCobrancaRequestDTO(
    string Nome,
    int PercentualDeMulta,
    int PercentualDeJurosAoMes,
    int CarenciaEmDias,
    int PercentualDeDescontoPorAntecipacao,
    int DiasMinimosParaDesconto
);

/// <summary>Corpo de inclusão e alteração de um item.</summary>
/// <param name="Tipo"><c>Mensalidade</c>, <c>Adesao</c>, <c>Rifa</c>, <c>ConviteExtra</c> ou <c>Avulsa</c>.</param>
/// <param name="Descricao">Nome na tela; ausente, vale o tipo.</param>
/// <param name="ValorEmCentavos">Valor <b>total</b> por formando, em centavos — <c>840000</c> é R$ 8.400,00. Negativo só em <c>Avulsa</c>.</param>
/// <param name="NumeroDeParcelas">Em quantas vezes, de 1 a 120.</param>
/// <param name="DiaDeVencimento">De 1 a 31; no mês mais curto, vale o último dia.</param>
/// <param name="PrimeiroMes">Mês do primeiro vencimento, <c>aaaa-mm-dd</c>; o dia é ignorado.</param>
/// <param name="AplicarAQuemJaAderiu">
/// Rateio extraordinário: grava a grade também para quem já aderiu. Exige
/// <paramref name="OrigemDaDecisao"/> e um primeiro mês que ainda não passou. Só na
/// <b>inclusão</b> — a alteração e a simulação ignoram.
/// </param>
/// <param name="OrigemDaDecisao">Onde a turma decidiu: "assembleia de 12/10". Até 200 caracteres.</param>
public sealed record ItemDeCobrancaRequestDTO(
    TipoDeCobranca Tipo,
    string? Descricao,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    int DiaDeVencimento,
    DateOnly PrimeiroMes,
    bool AplicarAQuemJaAderiu = false,
    string? OrigemDaDecisao = null
);

/// <summary>Corpo da simulação.</summary>
/// <param name="Itens">Itens a simular, gravados ou não. Ausente, simula os itens gravados do plano.</param>
public sealed record SimularPlanoRequestDTO(IReadOnlyList<ItemDeCobrancaRequestDTO>? Itens);

/// <summary>Plano na lista da turma.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome do plano.</param>
/// <param name="Status"><c>Rascunho</c> (em montagem) ou <c>Vigente</c>.</param>
/// <param name="VigenteDesde">Quando entrou em vigor, em UTC.</param>
public sealed record PlanoDeCobrancaResumoDTO(Guid Id, string Nome, StatusDoPlano Status, DateTime? VigenteDesde);

/// <summary>O plano inteiro.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome do plano.</param>
/// <param name="Status"><c>Rascunho</c> (em montagem) ou <c>Vigente</c>.</param>
/// <param name="VigenteDesde">Quando entrou em vigor, em UTC.</param>
/// <param name="PercentualDeMulta">Multa, base 10.000.</param>
/// <param name="PercentualDeJurosAoMes">Juros ao mês, base 10.000.</param>
/// <param name="CarenciaEmDias">Dias sem multa nem juros.</param>
/// <param name="PercentualDeDescontoPorAntecipacao">Desconto por antecipação, base 10.000.</param>
/// <param name="DiasMinimosParaDesconto">Dias de antecedência que o desconto exige.</param>
/// <param name="Itens">Itens, na ordem de criação, inclusive os encerrados.</param>
/// <param name="FormandosComParcela">Quantos já aderiram a este plano. Item incluído agora vale só para quem aderir depois, salvo no rateio.</param>
public sealed record PlanoDeCobrancaDTO(
    Guid Id,
    string Nome,
    StatusDoPlano Status,
    DateTime? VigenteDesde,
    int PercentualDeMulta,
    int PercentualDeJurosAoMes,
    int CarenciaEmDias,
    int PercentualDeDescontoPorAntecipacao,
    int DiasMinimosParaDesconto,
    IReadOnlyList<ItemDeCobrancaDTO> Itens,
    int FormandosComParcela
);

/// <summary>Um item do plano.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Tipo">O que cobra.</param>
/// <param name="Descricao">Nome na tela, se houver.</param>
/// <param name="ValorEmCentavos">Valor total por formando, em centavos.</param>
/// <param name="NumeroDeParcelas">Em quantas vezes.</param>
/// <param name="DiaDeVencimento">Dia do vencimento, de 1 a 31.</param>
/// <param name="PrimeiroMes">Mês do primeiro vencimento, no dia 1.</param>
/// <param name="EncerradoEm">Quando deixou de cobrar, se deixou.</param>
/// <param name="EmUso">Já gerou parcela: não se remove, só se encerra, e só o valor muda.</param>
/// <param name="OrigemDaDecisao">Onde a turma decidiu, se o item foi um rateio extraordinário; nulo no item comum.</param>
/// <param name="Opcional">Item opcional: só cobra quem pedir, e o valor é o preço unitário (Sprint 20).</param>
/// <param name="LimitePorFormando">Cota por pessoa, no item opcional.</param>
/// <param name="PedidosAteDia">Último dia para pedir, no item opcional.</param>
/// <param name="Estoque">Unidades existentes; nulo, sem teto.</param>
/// <param name="Reservados">Unidades já pedidas.</param>
/// <param name="AberturaDeVendas">A partir de quando se pode pedir.</param>
/// <param name="ItemDaFestaId">O item da festa que este item vende.</param>
/// <param name="ModoDeVenda"><c>AoFormando</c> ou <c>Publica</c> (a loja, Sprint 26).</param>
/// <param name="PrecoPublicoEmCentavos">Preço na loja, se diferente do do formando.</param>
public sealed record ItemDeCobrancaDTO(
    Guid Id,
    TipoDeCobranca Tipo,
    string? Descricao,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    int DiaDeVencimento,
    DateOnly PrimeiroMes,
    DateOnly? EncerradoEm,
    bool EmUso,
    string? OrigemDaDecisao,
    bool Opcional,
    int? LimitePorFormando,
    DateOnly? PedidosAteDia,
    int? Estoque,
    int Reservados,
    DateTime? AberturaDeVendas,
    Guid? ItemDaFestaId,
    ModoDeVenda ModoDeVenda,
    long? PrecoPublicoEmCentavos
);

/// <summary>Uma parcela da simulação.</summary>
/// <param name="Tipo">Tipo do item de origem.</param>
/// <param name="Descricao">Descrição do item de origem, se houver.</param>
/// <param name="Numero">Posição na grade do item.</param>
/// <param name="De">Total de parcelas do item.</param>
/// <param name="Vencimento">Dia do vencimento.</param>
/// <param name="ValorEmCentavos">Valor, em centavos.</param>
public sealed record ParcelaSimuladaDTO(TipoDeCobranca Tipo, string? Descricao, int Numero, int De, DateOnly Vencimento, long ValorEmCentavos);

/// <summary>A grade de um formando e o total da turma.</summary>
/// <param name="Parcelas">Parcelas de um formando, por vencimento.</param>
/// <param name="TotalPorFormando">Soma das parcelas, em centavos.</param>
/// <param name="Formandos">Membros ativos da turma hoje.</param>
/// <param name="TotalDaTurma">Total por formando vezes os formandos, em centavos.</param>
public sealed record SimulacaoDoPlanoDTO(IReadOnlyList<ParcelaSimuladaDTO> Parcelas, long TotalPorFormando, int Formandos, long TotalDaTurma);

/// <summary>Uma parcela, como a gestão e o próprio formando a veem.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="UsuarioId">Formando que deve.</param>
/// <param name="Nome">Nome civil do cadastro, ou o da conta.</param>
/// <param name="ItemDeCobrancaId">Item de origem — é por ele que a tela junta as parcelas de um pedido.</param>
/// <param name="Tipo">Tipo do item de origem.</param>
/// <param name="Descricao">Descrição do item de origem, se houver.</param>
/// <param name="Numero">Posição na grade do item.</param>
/// <param name="De">Total de parcelas do item.</param>
/// <param name="Vencimento">Dia do vencimento.</param>
/// <param name="ValorOriginalEmCentavos">Valor antes de multa e juros, em centavos.</param>
/// <param name="Status"><c>Aberta</c>, <c>Vencida</c>, <c>Paga</c> ou <c>Cancelada</c>, no dia de hoje.</param>
/// <param name="EmConferencia">Tem aviso de pagamento esperando a tesouraria ("Em conferência" na tela do formando).</param>
/// <param name="ValorPagoEmCentavos">O que entrou, se paga.</param>
/// <param name="PagoEm">Dia em que entrou, se paga.</param>
/// <param name="ValorDoDia">O valor de hoje, com a conta aberta — só na aberta e na vencida.</param>
/// <param name="RecebimentoId">A última baixa que vale — o recibo abre em <c>/recebimentos/{id}/recibo</c>. Nula sem baixa.</param>
/// <param name="PeloMercadoPago">Alguma baixa que vale veio do Mercado Pago — o estorno à mão só desfaz o registro (Sprint 42).</param>
public sealed record ParcelaDTO(
    Guid Id,
    Guid UsuarioId,
    string Nome,
    Guid ItemDeCobrancaId,
    TipoDeCobranca Tipo,
    string? Descricao,
    int Numero,
    int De,
    DateOnly Vencimento,
    long ValorOriginalEmCentavos,
    StatusDaParcela Status,
    bool EmConferencia,
    long? ValorPagoEmCentavos,
    DateOnly? PagoEm,
    ValorDoDiaDTO? ValorDoDia,
    Guid? RecebimentoId,
    bool PeloMercadoPago
);

/// <summary>O valor de uma parcela num dia, com a conta aberta — o que o formando vê ao tocar na parcela vencida.</summary>
/// <param name="OriginalEmCentavos">Valor antes de encargos.</param>
/// <param name="MultaEmCentavos">Multa, se o atraso passou da carência.</param>
/// <param name="JurosEmCentavos">Juros pro rata, se o atraso passou da carência.</param>
/// <param name="DescontoEmCentavos">Desconto por antecipação, antes do vencimento.</param>
/// <param name="TotalEmCentavos">O que se paga no dia — já abatido o <paramref name="JaPagoEmCentavos"/>.</param>
/// <param name="DiasDeAtraso">Dias depois do vencimento.</param>
/// <param name="JaPagoEmCentavos">O que já entrou por esta parcela em pagamentos parciais; zero na maioria.</param>
public sealed record ValorDoDiaDTO(
    long OriginalEmCentavos,
    long MultaEmCentavos,
    long JurosEmCentavos,
    long DescontoEmCentavos,
    long TotalEmCentavos,
    int DiasDeAtraso,
    long JaPagoEmCentavos
);

/// <summary>Quantas parcelas há numa situação e quanto somam.</summary>
/// <param name="Quantidade">Parcelas.</param>
/// <param name="ValorEmCentavos">Soma: o original, ou o que entrou nas pagas.</param>
public sealed record SomaDeParcelasDTO(int Quantidade, long ValorEmCentavos);

/// <summary>A faixa da tela Parcelas: quantas e quanto, por situação.</summary>
/// <param name="Todas">Todas as parcelas do filtro.</param>
/// <param name="Aberta">A vencer.</param>
/// <param name="Vencida">Vencidas, pelo original.</param>
/// <param name="Paga">Pagas, pelo que entrou.</param>
/// <param name="Cancelada">Canceladas.</param>
/// <param name="VencidoAtualizadoEmCentavos">As vencidas pelo valor de hoje, com multa e juros.</param>
public sealed record ResumoDeParcelasDTO(
    SomaDeParcelasDTO Todas,
    SomaDeParcelasDTO Aberta,
    SomaDeParcelasDTO Vencida,
    SomaDeParcelasDTO Paga,
    SomaDeParcelasDTO Cancelada,
    long VencidoAtualizadoEmCentavos
);
