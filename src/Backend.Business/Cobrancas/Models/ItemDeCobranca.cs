using Backend.Business.Abstractions;

namespace Backend.Business.Cobrancas.Models;

/// <summary>
/// Uma linha do plano: "Mensalidade, R$ 8.400,00 em 24×, todo dia 10, a partir de março".
/// </summary>
/// <remarks>
/// O valor é o <b>total por formando</b>, dividido pela grade (<see cref="GradeDeParcelas"/>). Com
/// o total guardado, "R$ 1.000,00 em 3×" fecha no centavo; com o valor da parcela guardado, o
/// centavo que sobra não teria onde morar.
/// </remarks>
public class ItemDeCobranca : EntidadeDaFormatura
{
    /// <summary>Plano dono.</summary>
    public Guid PlanoId { get; set; }

    /// <summary>O que cobra.</summary>
    public TipoDeCobranca Tipo { get; private set; }

    /// <summary>Nome na tela, se diferente do tipo.</summary>
    public string? Descricao { get; private set; }

    /// <summary>Valor total por formando, em centavos.</summary>
    public long ValorEmCentavos { get; private set; }

    /// <summary>Em quantas vezes.</summary>
    public int NumeroDeParcelas { get; private set; }

    /// <summary>Dia do vencimento, de 1 a 31. No mês que não tem o dia, vale o último.</summary>
    public int DiaDeVencimento { get; private set; }

    /// <summary>Mês do primeiro vencimento, sempre no dia 1.</summary>
    public DateOnly PrimeiroMes { get; private set; }

    /// <summary>Quando deixou de cobrar. Item encerrado não gera parcela nova.</summary>
    public DateOnly? EncerradoEm { get; private set; }

    /// <summary>
    /// Onde a turma decidiu este item — "assembleia de 12/10". Só no rateio extraordinário.
    /// </summary>
    /// <remarks>
    /// Preenchido quer dizer que o item alcançou também quem já tinha aderido (revisão de
    /// 17/09/2026 da decisão 8 da Sprint 7). É a prova da cobrança: o snapshot da adesão não a
    /// cita — ele é o que a pessoa leu no dia, e reescrevê-lo destruiria a defesa da comissão.
    /// </remarks>
    public string? OrigemDaDecisao { get; private set; }

    /// <summary>Cria o item a partir dos dados informados.</summary>
    /// <param name="planoId">Plano dono.</param>
    /// <param name="dados">Dados já validados.</param>
    /// <param name="origemDaDecisao">Onde a turma decidiu, no rateio extraordinário; nulo no item comum.</param>
    public static ItemDeCobranca Novo(Guid planoId, DadosDoItem dados, string? origemDaDecisao = null)
    {
        var item = new ItemDeCobranca { PlanoId = planoId, OrigemDaDecisao = origemDaDecisao?.Trim() };
        item.Aplicar(dados);

        return item;
    }

    /// <summary>Grava os dados informados.</summary>
    /// <param name="dados">Dados já validados.</param>
    public void Aplicar(DadosDoItem dados)
    {
        Tipo = dados.Tipo;
        Descricao = string.IsNullOrWhiteSpace(dados.Descricao) ? null : dados.Descricao.Trim();
        ValorEmCentavos = dados.ValorEmCentavos;
        NumeroDeParcelas = dados.NumeroDeParcelas;
        DiaDeVencimento = dados.DiaDeVencimento;
        PrimeiroMes = GradeDeParcelas.PrimeiroDoMes(dados.PrimeiroMes);
    }

    /// <summary>
    /// Se os dados mudam a forma da grade — e não só o valor.
    /// </summary>
    /// <remarks>
    /// Com parcela gerada, só o valor (e a descrição) pode mudar: trocar o número de parcelas ou o
    /// dia reescreveria vencimentos que alguém já pagou ou já tem na agenda.
    /// </remarks>
    /// <param name="dados">Dados pretendidos.</param>
    public bool MudaAGrade(DadosDoItem dados) =>
        dados.Tipo != Tipo
        || dados.NumeroDeParcelas != NumeroDeParcelas
        || dados.DiaDeVencimento != DiaDeVencimento
        || GradeDeParcelas.PrimeiroDoMes(dados.PrimeiroMes) != PrimeiroMes;

    /// <summary>Deixa de cobrar a partir de hoje. Encerrar de novo não muda a data.</summary>
    /// <param name="hoje">Dia do encerramento.</param>
    public void Encerrar(DateOnly hoje) => EncerradoEm ??= hoje;

    /// <summary>O item como dados — a forma que a grade e a simulação leem.</summary>
    public DadosDoItem ParaDados() => new(Tipo, Descricao, ValorEmCentavos, NumeroDeParcelas, DiaDeVencimento, PrimeiroMes);
}
