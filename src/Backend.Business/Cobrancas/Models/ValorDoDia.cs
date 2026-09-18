namespace Backend.Business.Cobrancas.Models;

/// <summary>As regras de atraso e antecipação que um formando aceitou.</summary>
/// <remarks>
/// Saem do snapshot da adesão, e não do plano atual: a tesouraria pode mudar o plano depois, e quem
/// já aderiu fica com o que aceitou. Percentuais em base 10.000, como no plano.
/// </remarks>
/// <param name="PercentualDeMulta">Multa por atraso, aplicada uma vez.</param>
/// <param name="PercentualDeJurosAoMes">Juros de mora ao mês.</param>
/// <param name="CarenciaEmDias">Dias depois do vencimento sem multa nem juros.</param>
/// <param name="PercentualDeDescontoPorAntecipacao">Desconto para quem paga antes do vencimento.</param>
/// <param name="DiasMinimosParaDesconto">
/// Dias de antecedência que o desconto exige. Zero é o comportamento anterior a 17/09/2026 — qualquer
/// dia antes do vencimento vale —, preservado para os snapshots já assinados.
/// </param>
public sealed record RegrasDeAtraso(
    int PercentualDeMulta,
    int PercentualDeJurosAoMes,
    int CarenciaEmDias,
    int PercentualDeDescontoPorAntecipacao,
    int DiasMinimosParaDesconto = 0
)
{
    /// <summary>Sem multa, juros nem desconto — a parcela vale o original em qualquer dia.</summary>
    public static readonly RegrasDeAtraso Nenhuma = new(0, 0, 0, 0);
}

/// <summary>
/// Quanto uma parcela vale num dia: o original, mais multa e juros se atrasou, menos o desconto se é
/// antes do vencimento.
/// </summary>
/// <remarks>
/// Calculado na leitura e nunca gravado (decisão 7 da Sprint 9): gravar exigiria um job reescrevendo
/// a dívida de todo mundo toda noite. É o valor do PIX, do extrato e do "devido" da conferência — a
/// mesma conta nos três.
/// </remarks>
/// <param name="OriginalEmCentavos">Valor da parcela antes de encargos.</param>
/// <param name="MultaEmCentavos">Multa, se o atraso passou da carência.</param>
/// <param name="JurosEmCentavos">Juros pro rata, se o atraso passou da carência.</param>
/// <param name="DescontoEmCentavos">Desconto por antecipação, se o dia é antes do vencimento.</param>
/// <param name="DiasDeAtraso">Dias depois do vencimento; zero no dia e antes dele.</param>
/// <param name="JaPagoEmCentavos">O que já entrou por esta parcela, em pagamentos parciais anteriores.</param>
public sealed record ValorDoDia(
    long OriginalEmCentavos,
    long MultaEmCentavos,
    long JurosEmCentavos,
    long DescontoEmCentavos,
    int DiasDeAtraso,
    long JaPagoEmCentavos = 0
)
{
    /// <summary>
    /// O que se paga no dia: o devido menos o que já entrou, sem virar crédito.
    /// </summary>
    /// <remarks>
    /// O piso é zero só quando o devido é positivo — a bolsa (<c>Avulsa</c> negativa) é um abatimento,
    /// e zerá-la aqui apagaria o desconto que a turma concedeu.
    /// </remarks>
    public long TotalEmCentavos => DevidoEmCentavos > 0 ? Math.Max(0, DevidoEmCentavos - JaPagoEmCentavos) : DevidoEmCentavos;

    /// <summary>O devido do dia sem abater o já pago — a base da divergência e do "de quanto era".</summary>
    public long DevidoEmCentavos => OriginalEmCentavos + MultaEmCentavos + JurosEmCentavos - DescontoEmCentavos;

    /// <summary>
    /// O valor da parcela no dia pedido.
    /// </summary>
    /// <remarks>
    /// Decisões de 14/09/2026: antes do vencimento, o original menos o desconto (P4); no dia do
    /// vencimento, o original; passada a carência, multa uma vez e juros simples de % ao mês ÷ 30 por
    /// dia, contados desde o vencimento (P5) — carência é tolerância, não prazo novo. Dentro da carência,
    /// nada. Os encargos incidem sobre o original, e cada parcela arredonda para o centavo mais
    /// próximo (meio centavo sobe).
    /// <para>
    /// Revisão de 17/09/2026: o desconto exige <see cref="RegrasDeAtraso.DiasMinimosParaDesconto"/>
    /// dias de antecedência. Sem isso, "5% para quem quitar à vista" virava 5% para quem pagou no dia
    /// 9 em vez do dia 10 — a turma inteira ganhando o desconto todo mês, que é dinheiro saindo do
    /// caixa por um campo que a tesouraria entendeu como outra coisa.
    /// </para>
    /// <para>Valor que não é positivo (o gancho da bolsa, em <c>Avulsa</c>) não tem encargo nem desconto.</para>
    /// </remarks>
    /// <param name="originalEmCentavos">Valor da parcela antes de encargos.</param>
    /// <param name="vencimento">Dia do vencimento.</param>
    /// <param name="dia">Dia do pagamento — hoje, para o PIX; o informado, para o devido da conferência.</param>
    /// <param name="regras">Regras do snapshot da adesão.</param>
    /// <param name="jaPagoEmCentavos">O que já entrou por esta parcela, em pagamentos parciais anteriores.</param>
    public static ValorDoDia Calcular(long originalEmCentavos, DateOnly vencimento, DateOnly dia, RegrasDeAtraso regras, long jaPagoEmCentavos = 0)
    {
        var atraso = Math.Max(0, dia.DayNumber - vencimento.DayNumber);

        if (originalEmCentavos <= 0)
            return new ValorDoDia(originalEmCentavos, 0, 0, 0, atraso, jaPagoEmCentavos);

        if (dia < vencimento)
        {
            var antecedencia = vencimento.DayNumber - dia.DayNumber;
            var desconto =
                antecedencia >= regras.DiasMinimosParaDesconto ? Percentual(originalEmCentavos, regras.PercentualDeDescontoPorAntecipacao) : 0;

            return new ValorDoDia(originalEmCentavos, 0, 0, desconto, 0, jaPagoEmCentavos);
        }

        if (atraso <= regras.CarenciaEmDias)
            return new ValorDoDia(originalEmCentavos, 0, 0, 0, atraso, jaPagoEmCentavos);

        var multa = Percentual(originalEmCentavos, regras.PercentualDeMulta);
        var juros = Arredondar(originalEmCentavos * (decimal)regras.PercentualDeJurosAoMes / 10_000m * atraso / 30m);

        return new ValorDoDia(originalEmCentavos, multa, juros, 0, atraso, jaPagoEmCentavos);
    }

    private static long Percentual(long centavos, int baseDezMil) => Arredondar(centavos * (decimal)baseDezMil / 10_000m);

    private static long Arredondar(decimal centavos) => (long)Math.Round(centavos, MidpointRounding.AwayFromZero);
}
