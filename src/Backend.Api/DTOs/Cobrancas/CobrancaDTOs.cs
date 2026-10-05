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

/// <summary>Corpo de inclusão e alteração de um pacote do catálogo — ou, com rateio, do item da assembleia.</summary>
/// <param name="Tipo">Categoria do pacote (<c>Festa</c>, <c>Colacao</c>, <c>FotoEAlbum</c>…); no rateio, <c>Mensalidade</c>, <c>Adesao</c>, <c>Rifa</c>, <c>ConviteExtra</c> ou <c>Avulsa</c>.</param>
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
/// <param name="Grupo">Grupo de faixas do pacote ("Festa"); ausente, pacote avulso. Na cesta, uma faixa por grupo.</param>
/// <param name="ConvitesDaFesta">Convites da festa que o pacote concede, de 0 a 100.</param>
/// <param name="ConvitesDaColacao">Convites da colação que o pacote concede, de 0 a 100.</param>
/// <param name="UltimoVencimento">
/// Até quando a última parcela pode vencer, <c>aaaa-mm-dd</c>; ausente, nada é conferido. Passou, 400
/// <c>cobranca.ultima_parcela_depois_do_limite</c>.
/// </param>
/// <param name="Alvo">
/// No rateio, os pacotes de quem paga — as faixas de "Festa", o pacote "Foto" (Sprint 48, D19). Ausente ou vazio: todos
/// os que já aderiram. Pacote fora do catálogo, 400 <c>cobranca.alvo_invalido</c>. Só na inclusão.
/// </param>
/// <param name="CancelavelAte">Último dia para o formando pedir o cancelamento do pacote, <c>aaaa-mm-dd</c>; ausente, sem trava (D36).</param>
/// <param name="AplicarAosAtuais">
/// Na alteração do preço de um item em uso: repactua também quem já aderiu, no que ainda não venceu (D21). Ausente, o
/// preço novo vale só para quem aderir depois.
/// </param>
public sealed record ItemDeCobrancaRequestDTO(
    TipoDeCobranca Tipo,
    string? Descricao,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    int DiaDeVencimento,
    DateOnly PrimeiroMes,
    bool AplicarAQuemJaAderiu = false,
    string? OrigemDaDecisao = null,
    string? Grupo = null,
    int ConvitesDaFesta = 0,
    int ConvitesDaColacao = 0,
    DateOnly? UltimoVencimento = null,
    IReadOnlyList<Guid>? Alvo = null,
    DateOnly? CancelavelAte = null,
    bool AplicarAosAtuais = false
)
{
    /// <summary>O corpo como o service o recebe.</summary>
    public DadosDoPacote ParaPacote() =>
        new(
            new DadosDoItem(Tipo, Descricao, ValorEmCentavos, NumeroDeParcelas, DiaDeVencimento, PrimeiroMes),
            Grupo,
            ConvitesDaFesta,
            ConvitesDaColacao,
            UltimoVencimento,
            CancelavelAte
        );
}

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
/// <param name="Pacote">Pacote do catálogo: só cobra quem o escolhe na adesão (Sprint 47).</param>
/// <param name="Grupo">Grupo de faixas do pacote; nulo é pacote avulso.</param>
/// <param name="ConvitesDaFesta">Convites da festa que o pacote concede.</param>
/// <param name="ConvitesDaColacao">Convites da colação que o pacote concede.</param>
/// <param name="UltimoVencimento">Até quando a última parcela pode vencer; nulo não confere.</param>
/// <param name="CancelavelAte">Último dia para o formando pedir o cancelamento; nulo, sem trava (Sprint 48, D36).</param>
/// <param name="AlvoDoRateio">Os pacotes de quem o rateio cobrou; vazio é todos (D19).</param>
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
    long? PrecoPublicoEmCentavos,
    bool Pacote,
    string? Grupo,
    int ConvitesDaFesta,
    int ConvitesDaColacao,
    DateOnly? UltimoVencimento,
    DateOnly? CancelavelAte,
    IReadOnlyList<Guid> AlvoDoRateio
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
/// <param name="SuspensaAte">
/// Até quando a parcela está fora da régua e da inadimplência: há solicitação de cancelamento esperando a comissão
/// (Sprint 48, D12). Nula, cobra normalmente.
/// </param>
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
    bool PeloMercadoPago,
    DateOnly? SuspensaAte
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

/// <summary>Quantos formandos uma operação alcança e quanto muda — a pergunta antes de confirmar (Sprint 48, D19/D21).</summary>
/// <param name="Formandos">Quantos formandos.</param>
/// <param name="Parcelas">Quantas parcelas mudam; zero no rateio, que nasce com a grade do item.</param>
/// <param name="TotalEmCentavos">Quanto a soma do que eles devem muda, em centavos.</param>
public sealed record AlcanceDTO(int Formandos, int Parcelas, long TotalEmCentavos);

/// <summary>Corpo do lançamento avulso no vínculo de um formando (Sprint 48, D23/D42).</summary>
/// <param name="UsuarioId">O formando.</param>
/// <param name="Descricao">O que é: "multa da mesa quebrada", "bolsa da comissão". Até 120 caracteres.</param>
/// <param name="ValorEmCentavos">Total, em centavos. Positivo cobra; negativo credita (bolsa, desconto).</param>
/// <param name="NumeroDeParcelas">Em quantas vezes, de 1 a 120, mensal.</param>
/// <param name="PrimeiroVencimento">
/// Dia da primeira parcela, <c>aaaa-mm-dd</c>; as seguintes vencem no mesmo dia dos meses seguintes. No passado, 400
/// <c>cobranca.lancamento_retroativo</c>.
/// </param>
public sealed record LancamentoAvulsoRequestDTO(
    Guid UsuarioId,
    string? Descricao,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    DateOnly PrimeiroVencimento
);

/// <summary>Um lançamento avulso, na lista da tesouraria.</summary>
/// <param name="ItemDeCobrancaId">O item do lançamento — encerrá-lo é <c>POST /cobrancas/planos/{plano_id}/itens/{id}/encerrar</c>.</param>
/// <param name="PlanoId">O plano em que nasceu.</param>
/// <param name="UsuarioId">O formando.</param>
/// <param name="Nome">Nome civil do cadastro, ou o da conta.</param>
/// <param name="Descricao">O que é.</param>
/// <param name="ValorEmCentavos">Total; negativo é crédito.</param>
/// <param name="NumeroDeParcelas">Em quantas vezes.</param>
/// <param name="PrimeiroVencimento">Dia da primeira parcela.</param>
/// <param name="LancadoEm">Quando foi lançado, em UTC.</param>
/// <param name="EncerradoEm">Quando foi encerrado, se foi.</param>
/// <param name="PagoEmCentavos">O que já entrou pelas parcelas dele.</param>
public sealed record LancamentoDTO(
    Guid ItemDeCobrancaId,
    Guid PlanoId,
    Guid UsuarioId,
    string Nome,
    string? Descricao,
    long ValorEmCentavos,
    int NumeroDeParcelas,
    DateOnly PrimeiroVencimento,
    DateTime LancadoEm,
    DateOnly? EncerradoEm,
    long PagoEmCentavos
);
