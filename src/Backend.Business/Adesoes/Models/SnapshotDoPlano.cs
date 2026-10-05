using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Business.Cobrancas.Models;
using Backend.Business.Common.Texto;

namespace Backend.Business.Adesoes.Models;

/// <summary>
/// O plano financeiro congelado no instante do aceite: o que exatamente o formando concordou em pagar.
/// </summary>
/// <remarks>
/// O plano muda — a mensalidade sobe em outubro, um item é encerrado. Sem esta cópia, "o que o João
/// aceitou pagar em março?" não teria resposta, e é a única pergunta que importa quando alguém
/// contesta a cobrança.
/// <para>
/// Gravado como JSON na adesão, com <see cref="VersaoDoEsquema"/>: a leitura ignora campo que não
/// conhece, e um campo novo no futuro não quebra o snapshot de hoje. Enum sai como texto — reordenar
/// <see cref="TipoDeCobranca"/> não pode transformar a mensalidade de ontem em rifa.
/// </para>
/// </remarks>
/// <param name="VersaoDoEsquema">Forma deste JSON. Hoje, <see cref="EsquemaAtual"/>.</param>
/// <param name="PlanoId">Plano de origem.</param>
/// <param name="NomeDoPlano">Nome do plano no dia do aceite.</param>
/// <param name="PercentualDeMulta">Multa por atraso, base 10.000.</param>
/// <param name="PercentualDeJurosAoMes">Juros de mora ao mês, base 10.000.</param>
/// <param name="CarenciaEmDias">Dias depois do vencimento sem multa nem juros.</param>
/// <param name="PercentualDeDescontoPorAntecipacao">Desconto por pagamento antecipado, base 10.000.</param>
/// <param name="Itens">Itens que cobravam, na ordem do plano.</param>
/// <param name="Parcelas">A grade do formando, por vencimento.</param>
/// <param name="TotalEmCentavos">Soma das parcelas.</param>
/// <param name="DiasMinimosParaDesconto">
/// Dias de antecedência que o desconto exige (revisão de 17/09/2026). Por último e com padrão, para
/// os snapshots já assinados continuarem lendo: neles o campo não existe, e zero é exatamente a regra
/// que aquelas pessoas aceitaram — qualquer dia antes do vencimento.
/// </param>
/// <param name="Cesta">
/// Os pacotes escolhidos — o quadro de escolhas do termo (Sprint 47, D3). Por último e com padrão, como o campo acima:
/// nos snapshots anteriores à cesta ele não existe, e a lista vazia é exatamente o que aquelas pessoas contrataram.
/// </param>
public sealed record SnapshotDoPlano(
    int VersaoDoEsquema,
    Guid PlanoId,
    string NomeDoPlano,
    int PercentualDeMulta,
    int PercentualDeJurosAoMes,
    int CarenciaEmDias,
    int PercentualDeDescontoPorAntecipacao,
    IReadOnlyList<DadosDoItem> Itens,
    IReadOnlyList<ParcelaSimulada> Parcelas,
    long TotalEmCentavos,
    int DiasMinimosParaDesconto = 0,
    IReadOnlyList<PacoteDaCesta>? Cesta = null
)
{
    /// <summary>Versão atual da forma do JSON.</summary>
    public const int EsquemaAtual = 1;

    /// <summary>
    /// Serialização fixa: a mesma entrada produz sempre o mesmo texto, e o hash do aceite é calculado
    /// sobre ele. Acento sai legível — o texto é lido no <c>psql</c>, nunca posto em HTML.
    /// </summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Congela o plano vigente com a cesta do formando, e a grade de quem adere no dia.</summary>
    /// <remarks>
    /// A grade sai por <c>GradeDeParcelas.DeQuemAdereEm</c>: quem adere depois do começo do plano
    /// deve o mesmo total, redividido pelas parcelas que ainda não venceram. É a mesma conta da
    /// geração — o que a pessoa lê e assina é, parcela por parcela, o que ela passa a dever.
    /// <para>
    /// Os itens são os da cesta (<see cref="PlanoDeCobranca.ItensDoFormando"/>) — o rateio é pontual (Sprint 48, D39) —, a mesma lista
    /// que a geração lê. A cesta entra também com os benefícios: "Festa 15 concede 15 convites" é contrato.
    /// </para>
    /// </remarks>
    /// <param name="plano">Plano em vigor, com os itens.</param>
    /// <param name="cesta">Pacotes escolhidos, já conferidos.</param>
    /// <param name="hoje">Dia da adesão.</param>
    public static SnapshotDoPlano De(PlanoDeCobranca plano, IReadOnlyCollection<ItemDeCobranca> cesta, DateOnly hoje)
    {
        var itens = plano.ItensDoFormando(cesta).Select(item => item.ParaDados()).ToList();
        var parcelas = GradeDeParcelas.DoFormando(itens, hoje);

        return new SnapshotDoPlano(
            EsquemaAtual,
            plano.Id,
            plano.Nome,
            plano.PercentualDeMulta,
            plano.PercentualDeJurosAoMes,
            plano.CarenciaEmDias,
            plano.PercentualDeDescontoPorAntecipacao,
            itens,
            parcelas,
            parcelas.Sum(parcela => parcela.ValorEmCentavos),
            plano.DiasMinimosParaDesconto,
            [.. plano.ItensDoFormando(cesta).Where(item => item.Pacote).Select(PacoteDaCesta.De)]
        );
    }

    /// <summary>Lê um snapshot gravado.</summary>
    /// <param name="json">Texto como está na adesão.</param>
    public static SnapshotDoPlano Ler(string json) =>
        JsonSerializer.Deserialize<SnapshotDoPlano>(json, Json) ?? throw new InvalidOperationException("Snapshot de plano vazio.");

    /// <summary>O texto que vai para a adesão — e para o hash do aceite.</summary>
    public string ParaJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>As regras de atraso aceitas — as que o valor do dia usa (Sprint 9).</summary>
    /// <remarks>Método, e não propriedade: propriedade entraria no JSON e mudaria o hash das adesões novas.</remarks>
    public RegrasDeAtraso Regras() =>
        new(PercentualDeMulta, PercentualDeJurosAoMes, CarenciaEmDias, PercentualDeDescontoPorAntecipacao, DiasMinimosParaDesconto);

    /// <summary>As parcelas do formando que saem de um item, por vencimento.</summary>
    /// <remarks>
    /// É daqui, e não do item, que saem "em quantas vezes" e o primeiro vencimento: quem adere tarde
    /// tem menos parcelas que o item. Método, pelo mesmo motivo de <see cref="Regras"/>.
    /// </remarks>
    /// <param name="item">Item do snapshot.</param>
    public IReadOnlyList<ParcelaSimulada> ParcelasDo(DadosDoItem item) =>
        [
            .. Parcelas.Where(parcela =>
                parcela.Tipo == item.Tipo && parcela.Descricao == (string.IsNullOrWhiteSpace(item.Descricao) ? null : item.Descricao.Trim())
            ),
        ];

    /// <summary>
    /// As regras de atraso numa frase — "multa de 2% e juros de 1% ao mês…" —, a mesma no PDF e no e-mail.
    /// </summary>
    public string RegrasDeAtrasoPorExtenso()
    {
        var atraso =
            PercentualDeMulta == 0 && PercentualDeJurosAoMes == 0
                ? "Sem multa nem juros em caso de atraso."
                : $"Em caso de atraso: multa de {FormatosBrasileiros.Percentual(PercentualDeMulta)} e juros de "
                    + $"{FormatosBrasileiros.Percentual(PercentualDeJurosAoMes)} ao mês"
                    + (CarenciaEmDias > 0 ? $", depois de {CarenciaEmDias} dias de carência." : ".");

        if (PercentualDeDescontoPorAntecipacao == 0)
            return atraso;

        var antecedencia = DiasMinimosParaDesconto > 0 ? $"com pelo menos {DiasMinimosParaDesconto} dias de antecedência" : "antes do vencimento";

        return $"{atraso} Desconto de {FormatosBrasileiros.Percentual(PercentualDeDescontoPorAntecipacao)} para pagamento {antecedencia}.";
    }
}

/// <summary>Um pacote como o termo o congela: o nome, o preço e o que ele concede (Sprint 47).</summary>
/// <param name="ItemId">Pacote de origem — é o que a re-adesão relê para manter a mesma cesta.</param>
/// <param name="Grupo">Grupo de faixas; nulo é pacote avulso.</param>
/// <param name="Tipo">Categoria do pacote.</param>
/// <param name="Descricao">Nome na tela, se houver.</param>
/// <param name="ValorEmCentavos">Preço total do pacote.</param>
/// <param name="ConvitesDaFesta">Convites da festa que o pacote concede.</param>
/// <param name="ConvitesDaColacao">Convites da colação que o pacote concede.</param>
public sealed record PacoteDaCesta(
    Guid ItemId,
    string? Grupo,
    TipoDeCobranca Tipo,
    string? Descricao,
    long ValorEmCentavos,
    int ConvitesDaFesta,
    int ConvitesDaColacao
)
{
    /// <summary>O pacote do catálogo, como entra no snapshot.</summary>
    /// <param name="item">Pacote.</param>
    public static PacoteDaCesta De(ItemDeCobranca item) =>
        new(item.Id, item.Grupo, item.Tipo, item.Descricao, item.ValorEmCentavos, item.ConvitesDaFesta, item.ConvitesDaColacao);

    /// <summary>"Festa — 15 pessoas": o grupo e a faixa, ou só o nome do pacote avulso.</summary>
    public string Rotulo() => Grupo is null ? RotuloDoItem.De(Tipo, Descricao) : $"{Grupo} — {RotuloDoItem.De(Tipo, Descricao)}";

    /// <summary>"15 convites da festa e 3 da colação", ou nulo se o pacote não concede convite.</summary>
    public string? BeneficiosPorExtenso() =>
        (ConvitesDaFesta, ConvitesDaColacao) switch
        {
            (0, 0) => null,
            (var festa, 0) => $"{festa} {(festa == 1 ? "convite" : "convites")} da festa",
            (0, var colacao) => $"{colacao} {(colacao == 1 ? "convite" : "convites")} da colação",
            var (festa, colacao) => $"{festa} {(festa == 1 ? "convite" : "convites")} da festa e {colacao} da colação",
        };
}
