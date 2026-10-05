using Backend.Business.Abstractions;

namespace Backend.Business.Cobrancas.Models;

/// <summary>
/// O que a turma decidiu cobrar: os itens e as regras de atraso.
/// </summary>
/// <remarks>
/// Desde a Sprint 47 é o <b>catálogo</b>: os pacotes de onde cada formando monta a cesta, os opcionais e os
/// rateios. "Um vigente por turma" continua — o que é único é o catálogo, não a dívida (D16).
/// <para>
/// É configuração, editável. O que uma pessoa deve é a <see cref="Parcela"/>, gravada na adesão
/// com o valor congelado — confundir os dois é o erro que faz "alterar a mensalidade" apagar o
/// histórico de pagamento da turma inteira.
/// </para>
/// <para>
/// As regras de atraso ficam aqui desde já e são aplicadas depois (baixa na Sprint 9, régua na
/// Sprint 13). Guardar a regra sem aplicá-la é barato; descobrir lá que o plano não tem onde
/// guardá-la custa uma migration com dinheiro dentro.
/// </para>
/// </remarks>
public class PlanoDeCobranca : EntidadeDaFormatura
{
    /// <summary>Nome do plano.</summary>
    public string Nome { get; set; } = string.Empty;

    /// <summary>Situação. Muda só por <see cref="Vigorar"/>.</summary>
    public StatusDoPlano Status { get; private set; } = StatusDoPlano.Rascunho;

    /// <summary>Quando entrou em vigor, em UTC.</summary>
    public DateTime? VigenteDesde { get; private set; }

    /// <summary>Multa por atraso, base 10.000 (<c>200</c> é 2%).</summary>
    public int PercentualDeMulta { get; set; }

    /// <summary>Juros de mora ao mês, base 10.000.</summary>
    public int PercentualDeJurosAoMes { get; set; }

    /// <summary>Dias depois do vencimento sem multa nem juros.</summary>
    public int CarenciaEmDias { get; set; }

    /// <summary>Desconto para pagamento antecipado, base 10.000.</summary>
    public int PercentualDeDescontoPorAntecipacao { get; set; }

    /// <summary>Dias de antecedência que o desconto exige. Zero: qualquer dia antes do vencimento.</summary>
    public int DiasMinimosParaDesconto { get; set; }

    /// <summary>Itens do plano, inclusive os encerrados.</summary>
    public List<ItemDeCobranca> Itens { get; private set; } = [];

    /// <summary>Os pacotes à venda no catálogo, na ordem em que foram criados.</summary>
    /// <remarks>
    /// Ordem fixa, com desempate pelo id: o catálogo da adesão, a grade e o texto aceito saem sempre iguais. O filtro
    /// de <c>Opcional</c> mora em <see cref="ItemDeCobranca.Pacote"/> — esquecê-lo poria o convite extra na cesta.
    /// </remarks>
    public IReadOnlyList<ItemDeCobranca> Pacotes() =>
        [.. Itens.Where(item => item.EncerradoEm is null && item.Pacote).OrderBy(i => i.CriadoEm).ThenBy(i => i.Id)];

    /// <summary>Os itens opcionais que ainda estão à venda — o que o pedido alcança.</summary>
    public IEnumerable<ItemDeCobranca> ItensOpcionais => Itens.Where(item => item.EncerradoEm is null && item.Opcional);

    /// <summary>
    /// O que um formando passa a dever na adesão: os pacotes da cesta, na ordem do plano.
    /// </summary>
    /// <remarks>
    /// Só a cesta. O rateio extraordinário é pontual (Sprint 48, D22/D39): cobra quem tem o alvo no dia do lançamento,
    /// e quem adere depois paga o preço vigente do catálogo — que a comissão ajusta, se quiser (D21). Até a Sprint 47,
    /// o rateio ativo alcançava também quem aderia depois.
    /// </remarks>
    /// <param name="cesta">Pacotes escolhidos, já conferidos por <see cref="MontarCesta"/>.</param>
    public IReadOnlyList<ItemDeCobranca> ItensDoFormando(IReadOnlyCollection<ItemDeCobranca> cesta) =>
        [.. Itens.Where(cesta.Contains).OrderBy(i => i.CriadoEm).ThenBy(i => i.Id)];

    /// <summary>
    /// Confere a escolha do formando contra o catálogo: pacotes que existem, sem repetição, uma faixa por grupo (D32).
    /// </summary>
    /// <remarks>
    /// A cesta vazia passa aqui — a prévia do termo a mostra antes da escolha. Quem exige ao menos um pacote é a adesão
    /// (D33).
    /// </remarks>
    /// <param name="pacoteIds">Ids escolhidos.</param>
    /// <returns>Os pacotes, na ordem do catálogo.</returns>
    public Result<IReadOnlyList<ItemDeCobranca>> MontarCesta(IReadOnlyCollection<Guid> pacoteIds)
    {
        if (pacoteIds.Distinct().Count() != pacoteIds.Count)
            return Erro.Validacao("cobranca.cesta_duplicada", "O mesmo pacote foi escolhido duas vezes.", campo: "pacotes");

        var catalogo = Pacotes();
        var cesta = catalogo.Where(pacote => pacoteIds.Contains(pacote.Id)).ToList();

        if (cesta.Count != pacoteIds.Count)
            return Erro.Validacao("cobranca.pacote_invalido", "Um dos pacotes escolhidos não está mais no catálogo da turma.", campo: "pacotes");

        if (
            cesta.Where(pacote => pacote.Grupo is not null).GroupBy(pacote => pacote.Grupo).FirstOrDefault(grupo => grupo.Count() > 1) is { } repetido
        )
            return Erro.Validacao("cobranca.faixa_invalida", $"Escolha só uma faixa de {repetido.Key}.", campo: "pacotes");

        return cesta;
    }

    /// <summary>
    /// A cesta de quem adere: a que ele já contratou numa adesão anterior, ou a que escolheu agora.
    /// </summary>
    /// <remarks>
    /// A re-adesão a uma versão nova do termo não muda o contrato (D4): a cesta contratada vale, mesmo que um pacote
    /// tenha sido encerrado depois. Trocar de faixa é a Sprint 48.
    /// </remarks>
    /// <param name="contratada">Pacotes já gravados na cesta do vínculo.</param>
    /// <param name="escolhida">Pacotes escolhidos na tela.</param>
    public Result<IReadOnlyList<ItemDeCobranca>> CestaDe(IReadOnlyCollection<Guid> contratada, IReadOnlyCollection<Guid> escolhida) =>
        contratada.Count > 0
            ? Result.Ok<IReadOnlyList<ItemDeCobranca>>([.. Itens.Where(item => contratada.Contains(item.Id))])
            : MontarCesta(escolhida);

    /// <summary>Grava nome e regras de atraso.</summary>
    /// <param name="dados">Dados já validados.</param>
    public void Aplicar(DadosDoPlano dados)
    {
        Nome = dados.Nome.Trim();
        PercentualDeMulta = dados.PercentualDeMulta;
        PercentualDeJurosAoMes = dados.PercentualDeJurosAoMes;
        CarenciaEmDias = dados.CarenciaEmDias;
        PercentualDeDescontoPorAntecipacao = dados.PercentualDeDescontoPorAntecipacao;
        DiasMinimosParaDesconto = dados.DiasMinimosParaDesconto;
    }

    /// <summary>Coloca o plano em vigor: a partir daqui, a adesão gera parcelas por ele.</summary>
    /// <remarks>
    /// "Só um vigente por turma" não mora aqui — depende dos outros planos, e é conferido pelo
    /// service e, na corrida, pelo índice único parcial do banco.
    /// </remarks>
    /// <param name="agoraUtc">Momento da decisão.</param>
    public Result Vigorar(DateTime agoraUtc)
    {
        if (Status != StatusDoPlano.Rascunho)
            return Result.Falha(Erro.Conflito("cobranca.plano_ja_vigente", "Este plano já está em vigor."));

        if (Pacotes().Count == 0)
            return Result.Falha(Erro.Conflito("cobranca.plano_sem_itens", "Inclua ao menos um pacote antes de colocar o plano em vigor."));

        Status = StatusDoPlano.Vigente;
        VigenteDesde = agoraUtc;

        return Result.Ok();
    }
}

/// <summary>Situação do plano de cobrança.</summary>
/// <remarks>
/// Gravado como texto: o índice único parcial filtra por <c>status = 'Vigente'</c>. Na tela,
/// <see cref="Rascunho"/> se lê "Em montagem".
/// </remarks>
public enum StatusDoPlano
{
    /// <summary>Em montagem: a tesouraria ajusta os itens e confere a simulação.</summary>
    Rascunho,

    /// <summary>Em vigor. A adesão gera parcelas por ele. No máximo um por turma.</summary>
    Vigente,
}
