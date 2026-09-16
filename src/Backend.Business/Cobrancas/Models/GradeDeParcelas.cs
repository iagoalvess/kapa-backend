namespace Backend.Business.Cobrancas.Models;

/// <summary>Uma linha da grade: o que um formando deve, em que dia.</summary>
/// <param name="Numero">Posição na grade do item, a partir de 1.</param>
/// <param name="Vencimento">Dia do vencimento.</param>
/// <param name="ValorEmCentavos">Valor da parcela, em centavos.</param>
public sealed record ParcelaPrevista(int Numero, DateOnly Vencimento, long ValorEmCentavos);

/// <summary>
/// O cálculo da grade de um item — o único que existe.
/// </summary>
/// <remarks>
/// A simulação, a geração das parcelas e a repactuação passam todas por aqui. Duas contas que
/// "fazem a mesma coisa" discordam um dia, e aí o formando vê na prévia um valor que não é o que
/// ele deve.
/// </remarks>
public static class GradeDeParcelas
{
    /// <summary>
    /// Divide o valor do item em parcelas mensais.
    /// </summary>
    /// <remarks>
    /// Divisão inteira em centavos, com o resto na <b>primeira</b> parcela: R$ 1.000,00 em 3 são
    /// R$ 333,34 + R$ 333,33 + R$ 333,33, e a soma fecha no centavo. O resto vai na primeira porque
    /// é a que já se paga na adesão — ninguém descobre um centavo a mais na última, dois anos depois.
    /// <para>
    /// Valor negativo (bolsa, em <see cref="TipoDeCobranca.Avulsa"/>) funciona igual: a divisão do
    /// C# trunca em direção a zero e o resto sai com o sinal do valor.
    /// </para>
    /// </remarks>
    /// <param name="item">Item já validado — com ao menos uma parcela.</param>
    public static IReadOnlyList<ParcelaPrevista> Calcular(DadosDoItem item)
    {
        var basica = item.ValorEmCentavos / item.NumeroDeParcelas;
        var resto = item.ValorEmCentavos % item.NumeroDeParcelas;
        var primeiroMes = PrimeiroDoMes(item.PrimeiroMes);

        return
        [
            .. Enumerable
                .Range(1, item.NumeroDeParcelas)
                .Select(numero => new ParcelaPrevista(
                    numero,
                    Vencimento(primeiroMes.AddMonths(numero - 1), item.DiaDeVencimento),
                    numero == 1 ? basica + resto : basica
                )),
        ];
    }

    /// <summary>
    /// A grade inteira de um formando: as parcelas de todos os itens, por vencimento.
    /// </summary>
    /// <remarks>
    /// É a prévia da tesouraria e o plano que o formando aceita na adesão — a mesma lista, para o que
    /// ele leu ser o que ele deve. Ordenação estável: parcelas do mesmo dia seguem a ordem dos itens.
    /// </remarks>
    /// <param name="itens">Itens já validados, na ordem do plano.</param>
    public static IReadOnlyList<ParcelaSimulada> DoFormando(IEnumerable<DadosDoItem> itens) =>
        [
            .. itens
                .SelectMany(item =>
                    Calcular(item)
                        .Select(parcela => new ParcelaSimulada(
                            item.Tipo,
                            string.IsNullOrWhiteSpace(item.Descricao) ? null : item.Descricao.Trim(),
                            parcela.Numero,
                            item.NumeroDeParcelas,
                            parcela.Vencimento,
                            parcela.ValorEmCentavos
                        ))
                )
                .OrderBy(parcela => parcela.Vencimento),
        ];

    /// <summary>O vencimento no mês, com o dia limitado ao último dia dele.</summary>
    /// <remarks>
    /// Dia 31 em abril vira 30; em fevereiro, 28 — ou 29 no ano bissexto. A turma escolhe qualquer
    /// dia de 1 a 31 (decisão de 14/09/2026), e o mês curto não pula o vencimento para o seguinte.
    /// </remarks>
    /// <param name="mes">Qualquer dia do mês.</param>
    /// <param name="dia">Dia pretendido, de 1 a 31.</param>
    public static DateOnly Vencimento(DateOnly mes, int dia) => new(mes.Year, mes.Month, Math.Min(dia, DateTime.DaysInMonth(mes.Year, mes.Month)));

    /// <summary>O dia 1 do mês da data — o mês do primeiro vencimento é gravado assim.</summary>
    /// <param name="data">Qualquer dia do mês.</param>
    public static DateOnly PrimeiroDoMes(DateOnly data) => new(data.Year, data.Month, 1);
}
