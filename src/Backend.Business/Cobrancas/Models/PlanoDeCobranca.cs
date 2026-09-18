using Backend.Business.Abstractions;

namespace Backend.Business.Cobrancas.Models;

/// <summary>
/// O que a turma decidiu cobrar: os itens e as regras de atraso.
/// </summary>
/// <remarks>
/// É configuração, editável. O que uma pessoa deve é a <see cref="Parcela"/>, gravada na adesão
/// com o valor congelado — confundir os dois é o erro que faz "alterar a mensalidade" apagar o
/// histórico de pagamento da turma inteira.
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

    /// <summary>Itens que ainda cobram.</summary>
    public IEnumerable<ItemDeCobranca> ItensAtivos => Itens.Where(item => item.EncerradoEm is null);

    /// <summary>Os itens que ainda cobram, como dados, na ordem em que foram criados.</summary>
    /// <remarks>Ordem fixa, com desempate pelo id: a grade e o texto aceito na adesão saem sempre iguais.</remarks>
    public IReadOnlyList<DadosDoItem> DadosDosItensAtivos() => [.. ItensAtivos.OrderBy(i => i.CriadoEm).ThenBy(i => i.Id).Select(i => i.ParaDados())];

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

    /// <summary>
    /// Confere se um item deste tipo cabe no plano.
    /// </summary>
    /// <remarks>
    /// A adesão é única: duas taxas de entrada no mesmo plano é erro de digitação, nunca intenção. O
    /// item encerrado não conta — é o caminho para trocar a taxa depois que ela já foi cobrada.
    /// </remarks>
    /// <param name="tipo">Tipo do item novo ou alterado.</param>
    /// <param name="exceto">O item em alteração, que não conflita consigo mesmo.</param>
    public Result AceitaItem(TipoDeCobranca tipo, ItemDeCobranca? exceto = null)
    {
        if (tipo == TipoDeCobranca.Adesao && ItensAtivos.Any(item => item.Tipo == TipoDeCobranca.Adesao && item != exceto))
            return Result.Falha(Erro.Conflito("cobranca.adesao_duplicada", "Este plano já tem uma taxa de adesão."));

        return Result.Ok();
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

        if (!ItensAtivos.Any())
            return Result.Falha(Erro.Conflito("cobranca.plano_sem_itens", "Inclua ao menos um item antes de colocar o plano em vigor."));

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
