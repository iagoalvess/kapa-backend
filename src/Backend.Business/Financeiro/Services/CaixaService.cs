using Backend.Business.Abstractions;
using Backend.Business.Common.Datas;
using Backend.Business.Financeiro.Interfaces;
using Backend.Business.Financeiro.Models;
using Backend.Business.Formaturas.Interfaces;

namespace Backend.Business.Financeiro.Services;

/// <summary>
/// Quanto a turma tem, quanto ainda entra e quanto ainda sai.
/// </summary>
/// <remarks>
/// Saldo é agregação de lançamento, nunca coluna (decisão 1): arrecadado (recebimentos da Sprint 9 e
/// receitas recebidas da Sprint 28) menos gasto (despesas pagas), calculado a cada consulta. A projeção conta só o que ainda vence
/// (decisão 6): parcela vencida vai à parte, despesa prevista atrasada cai no mês atual. Receita
/// prevista entra só na <see cref="Projecao"/> (P2 da Sprint 28), e a atrasada fica fora, como a
/// parcela vencida: dinheiro prometido que não veio não é conta com que se possa contar.
/// </remarks>
/// <param name="caixaRepository">As agregações do caixa.</param>
/// <param name="formaturaRepository">Data da colação — o fim do horizonte da projeção.</param>
public sealed class CaixaService(ICaixaRepository caixaRepository, IFormaturaRepository formaturaRepository) : ICaixaService
{
    /// <summary>Lançamentos no rodapé da tela do caixa.</summary>
    public const int UltimosLancamentos = 8;

    /// <summary>
    /// Teto de meses da projeção.
    /// </summary>
    /// <remarks>
    /// Dez anos. Data digitada errada não pode virar um gráfico de trezentas colunas — e turma nenhuma
    /// se forma daqui a mais que isso.
    /// </remarks>
    public const int MaximoDeMeses = 120;

    /// <summary>Os meses já fechados do gráfico do Início, o atual incluído — o próximo vem à parte.</summary>
    private const int MesesDaArrecadacao = 5;

    /// <inheritdoc />
    public async Task<Result<CaixaConsolidado>> Consolidado(CancellationToken ct = default)
    {
        var hoje = DataUtils.Hoje();
        var (aVencer, emAtraso) = await caixaRepository.ParcelasEmAberto(hoje, ct);

        return new CaixaConsolidado(
            await caixaRepository.Arrecadado(ct),
            await caixaRepository.Gasto(ct),
            aVencer,
            emAtraso,
            await caixaRepository.DespesasPrevistas(ct),
            await caixaRepository.PorCategoria(ct),
            await caixaRepository.OutrasReceitasPorCategoria(ct),
            await caixaRepository.UltimosLancamentos(UltimosLancamentos, ct)
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// O acumulado começa em zero no primeiro mês com movimento e chega, no mês de hoje, ao saldo real
    /// da turma — é a mesma conta da tela do caixa, desenhada no tempo. Daí para a frente ele soma o
    /// previsto, e é essa parte que a tela desenha pontilhada.
    /// </remarks>
    public async Task<Result<ProjecaoDoCaixa>> Projecao(Guid formaturaId, CancellationToken ct = default)
    {
        var hoje = DataUtils.Hoje();
        var mesAtual = PrimeiroDoMes(hoje);

        var entradas = Indexar(await caixaRepository.EntradasPorMes(ct));
        var saidas = Indexar(await caixaRepository.SaidasPorMes(ct));
        var entradasPrevistas = Indexar(await caixaRepository.EntradasPrevistasPorMes(hoje, comOutrasReceitasPrevistas: true, ct));
        var saidasPrevistas = Indexar(await caixaRepository.SaidasPrevistasPorMes(hoje, ct));

        var colacao = (await formaturaRepository.ObterDetalheDeTodasAsFormaturas(formaturaId, ct))?.PrevisaoDeColacao;
        var meses = Janela(mesAtual, colacao, [entradas, saidas, entradasPrevistas, saidasPrevistas]);

        var acumulado = 0L;
        var linhas = new List<MesDoCaixa>(meses.Count);

        foreach (var mes in meses)
        {
            var entrada = entradas.GetValueOrDefault(mes);
            var saida = saidas.GetValueOrDefault(mes);
            var entradaPrevista = entradasPrevistas.GetValueOrDefault(mes);
            var saidaPrevista = saidasPrevistas.GetValueOrDefault(mes);

            acumulado += entrada - saida + entradaPrevista - saidaPrevista;

            linhas.Add(new MesDoCaixa(mes, entrada, saida, entradaPrevista, saidaPrevista, acumulado, mes > mesAtual));
        }

        var (_, emAtraso) = await caixaRepository.ParcelasEmAberto(hoje, ct);

        return new ProjecaoDoCaixa(linhas, await caixaRepository.Arrecadado(ct) - await caixaRepository.Gasto(ct), emAtraso);
    }

    /// <inheritdoc />
    /// <remarks>
    /// O acumulado começa antes da janela: o primeiro mês do gráfico já traz tudo o que veio antes.
    /// </remarks>
    public async Task<Result<IReadOnlyList<MesDaArrecadacao>>> Arrecadacao(CancellationToken ct = default)
    {
        var hoje = DataUtils.Hoje();
        var mesAtual = PrimeiroDoMes(hoje);
        var entradas = Indexar(await caixaRepository.EntradasPorMes(ct));
        var previstas = Indexar(await caixaRepository.EntradasPrevistasPorMes(hoje, comOutrasReceitasPrevistas: false, ct));

        MesDaArrecadacao Fechamento(DateOnly mes) => new(mes, entradas.Where(entrada => entrada.Key <= mes).Sum(entrada => entrada.Value), false);

        var passados = Enumerable.Range(0, MesesDaArrecadacao).Select(atras => Fechamento(mesAtual.AddMonths(atras - MesesDaArrecadacao + 1)));
        var proximo = mesAtual.AddMonths(1);
        var jaJuntado = entradas.Values.Sum();

        return Result.Ok<IReadOnlyList<MesDaArrecadacao>>([
            .. passados,
            new MesDaArrecadacao(proximo, jaJuntado + previstas.GetValueOrDefault(proximo), true),
        ]);
    }

    /// <summary>Os meses do gráfico, sem buraco: do primeiro com movimento ao último previsto — ou à colação.</summary>
    /// <param name="mesAtual">Mês de hoje, sempre presente.</param>
    /// <param name="colacao">Data da colação, se houver.</param>
    /// <param name="series">Séries já indexadas por mês.</param>
    private static List<DateOnly> Janela(DateOnly mesAtual, DateOnly? colacao, IReadOnlyList<Dictionary<DateOnly, long>> series)
    {
        var chaves = series.SelectMany(serie => serie.Keys).ToList();

        var inicio = chaves.Count == 0 ? mesAtual : Menor(chaves.Min(), mesAtual);
        var fim = chaves.Count == 0 ? mesAtual : Maior(chaves.Max(), mesAtual);

        if (colacao is { } dia)
            fim = Maior(fim, PrimeiroDoMes(dia));

        var total = Math.Min(((fim.Year - inicio.Year) * 12) + fim.Month - inicio.Month + 1, MaximoDeMeses);

        return [.. Enumerable.Range(0, total).Select(inicio.AddMonths)];
    }

    private static Dictionary<DateOnly, long> Indexar(IReadOnlyList<SomaDoMes> somas) =>
        somas.ToDictionary(soma => PrimeiroDoMes(soma.Mes), soma => soma.ValorEmCentavos);

    private static DateOnly PrimeiroDoMes(DateOnly data) => new(data.Year, data.Month, 1);

    private static DateOnly Menor(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateOnly Maior(DateOnly a, DateOnly b) => a > b ? a : b;
}
