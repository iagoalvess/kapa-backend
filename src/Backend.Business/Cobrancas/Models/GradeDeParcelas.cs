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
        var valores = Distribuir(item.ValorEmCentavos, item.NumeroDeParcelas);
        var primeiroMes = PrimeiroDoMes(item.PrimeiroMes);

        return
        [
            .. Enumerable
                .Range(1, item.NumeroDeParcelas)
                .Select(numero => new ParcelaPrevista(
                    numero,
                    Vencimento(primeiroMes.AddMonths(numero - 1), item.DiaDeVencimento),
                    valores[numero - 1]
                )),
        ];
    }

    /// <summary>
    /// Divide um total em partes iguais de centavos, com o resto na primeira.
    /// </summary>
    /// <remarks>
    /// A conta de <see cref="Calcular"/>, isolada porque a repactuação e a grade de quem adere
    /// depois precisam dela sobre outra quantidade de parcelas. Divisão do C#: trunca em direção a
    /// zero, e o resto sai com o sinal do valor — o negativo da bolsa funciona igual.
    /// </remarks>
    /// <param name="total">Valor a dividir, em centavos.</param>
    /// <param name="partes">Em quantas, ao menos uma.</param>
    public static IReadOnlyList<long> Distribuir(long total, int partes)
    {
        var basica = total / partes;
        var resto = total % partes;

        return [.. Enumerable.Range(1, partes).Select(posicao => posicao == 1 ? basica + resto : basica)];
    }

    /// <summary>
    /// A grade de um item para quem adere <paramref name="hoje"/>: o mesmo total, redividido pelas
    /// parcelas que ainda não venceram.
    /// </summary>
    /// <remarks>
    /// Quem adere no dia da publicação recebe a grade inteira — nada muda para ele. Quem adere em
    /// setembro num plano que começou em março deve o mesmo total dos colegas, só que em menos
    /// vezes: paga mais por mês, e nenhuma parcela nasce vencida.
    /// <para>
    /// O contrário — gravar a grade inteira — é o que o sistema fazia até 17/09/2026: seis parcelas
    /// nasciam vencidas, com multa e juros de um atraso que a pessoa não teve como cometer, e a
    /// régua começava a cobrar no dia seguinte.
    /// </para>
    /// <para>
    /// Os vencimentos continuam os do item; a numeração é a da pessoa, de 1 em diante (28/09/2026):
    /// quem tem 12 parcelas vê "1/12" a "12/12". Até então ela herdava a posição no item, e a lista
    /// começava em "9/20" — quem entrou tarde procurava as parcelas 1 a 8 que não devia. A comissão
    /// compara colegas pelo vencimento, não pelo número. Se nenhum vencimento sobrou — a pessoa
    /// adere depois do último —, o total inteiro vira uma parcela, no próximo dia de vencimento a
    /// partir de hoje.
    /// </para>
    /// </remarks>
    /// <param name="item">Item já validado.</param>
    /// <param name="hoje">Dia da adesão.</param>
    public static IReadOnlyList<ParcelaPrevista> DeQuemAdereEm(DadosDoItem item, DateOnly hoje)
    {
        var grade = Calcular(item);
        var restantes = grade.Where(parcela => parcela.Vencimento >= hoje).ToList();

        if (restantes.Count == 0)
            restantes = [new ParcelaPrevista(1, Vencimento(ProximoMesComODia(hoje, item.DiaDeVencimento), item.DiaDeVencimento), 0)];

        if (restantes.Count == grade.Count)
            return grade;

        var valores = Distribuir(item.ValorEmCentavos, restantes.Count);

        return [.. restantes.Select((parcela, posicao) => parcela with { Numero = posicao + 1, ValorEmCentavos = valores[posicao] })];
    }

    /// <summary>O mês em que o dia de vencimento ainda acontece a partir de hoje — este, ou o que vem.</summary>
    /// <param name="hoje">Dia de referência.</param>
    /// <param name="dia">Dia do vencimento, de 1 a 31.</param>
    private static DateOnly ProximoMesComODia(DateOnly hoje, int dia) =>
        Vencimento(hoje, dia) >= hoje ? PrimeiroDoMes(hoje) : PrimeiroDoMes(hoje).AddMonths(1);

    /// <summary>
    /// A grade inteira de um formando: as parcelas de todos os itens, por vencimento.
    /// </summary>
    /// <remarks>
    /// É a prévia da tesouraria e o plano que o formando aceita na adesão — a mesma lista, para o que
    /// ele leu ser o que ele deve. Ordenação estável: parcelas do mesmo dia seguem a ordem dos itens.
    /// </remarks>
    /// <param name="itens">Itens já validados, na ordem do plano.</param>
    /// <param name="hoje">
    /// Dia da adesão, para a grade sair como quem adere hoje vai devê-la
    /// (<see cref="DeQuemAdereEm"/>). Ausente, a grade cheia do plano — é a prévia da tesouraria,
    /// que descreve o plano e não uma pessoa.
    /// </param>
    public static IReadOnlyList<ParcelaSimulada> DoFormando(IEnumerable<DadosDoItem> itens, DateOnly? hoje = null) =>
        [
            .. itens
                .SelectMany(item =>
                {
                    var grade = hoje is { } dia ? DeQuemAdereEm(item, dia) : Calcular(item);

                    return grade.Select(parcela => new ParcelaSimulada(
                        item.Tipo,
                        string.IsNullOrWhiteSpace(item.Descricao) ? null : item.Descricao.Trim(),
                        parcela.Numero,
                        grade.Count,
                        parcela.Vencimento,
                        parcela.ValorEmCentavos
                    ));
                })
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
