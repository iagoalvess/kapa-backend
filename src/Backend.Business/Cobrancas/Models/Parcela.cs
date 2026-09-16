using Backend.Business.Abstractions;

namespace Backend.Business.Cobrancas.Models;

/// <summary>
/// O que uma pessoa deve, num dia. Fato gravado, não cálculo.
/// </summary>
/// <remarks>
/// Uma linha por vencimento, gravada na adesão (Sprint 7) com o valor congelado. Se a grade fosse
/// calculada a cada consulta, mudar o plano em outubro mudaria o extrato de março — e quem já pagou
/// passaria a dever diferente do que pagou.
/// <para>
/// A chave natural <c>(VinculoId, ItemDeCobrancaId, Numero)</c> tem índice único: clique duplo,
/// retry do cliente e reprocessamento não geram a segunda cobrança do mesmo mês.
/// </para>
/// <para>
/// <see cref="ValorOriginalEmCentavos"/> é o valor antes de multa e juros. O valor do dia é calculado
/// na consulta (<see cref="ValorEm"/>), nunca gravado. O que se grava é o fato: quanto entrou e em que
/// dia (<see cref="ValorPagoEmCentavos"/>, <see cref="PagoEm"/>), pela baixa da tesouraria.
/// </para>
/// </remarks>
public class Parcela : EntidadeDaFormatura
{
    /// <summary>Vínculo de quem deve.</summary>
    public Guid VinculoId { get; set; }

    /// <summary>Item de origem.</summary>
    public Guid ItemDeCobrancaId { get; set; }

    /// <summary>Posição na grade do item, a partir de 1.</summary>
    public int Numero { get; set; }

    /// <summary>Dia do vencimento. <see cref="DateOnly"/>: fuso nenhum move o dia.</summary>
    public DateOnly Vencimento { get; set; }

    /// <summary>
    /// Valor antes de multa e juros, em centavos.
    /// </summary>
    /// <remarks>Muda só por <see cref="Repactuar"/>, e só enquanto a parcela não venceu.</remarks>
    public long ValorOriginalEmCentavos { get; private set; }

    /// <summary>Situação gravada. <see cref="StatusEm"/> diz a do dia.</summary>
    public StatusDaParcela Status { get; private set; } = StatusDaParcela.Aberta;

    /// <summary>Quanto entrou na conta da turma, em centavos. Só na parcela paga.</summary>
    /// <remarks>Pode diferir do devido: a diferença vai para Divergências, não para um saldo (decisão 5 da Sprint 9).</remarks>
    public long? ValorPagoEmCentavos { get; private set; }

    /// <summary>Dia em que o dinheiro entrou. Só na parcela paga.</summary>
    public DateOnly? PagoEm { get; private set; }

    /// <summary>Cria a parcela de um vínculo a partir de uma linha da grade.</summary>
    /// <param name="vinculoId">Quem deve.</param>
    /// <param name="itemId">Item de origem.</param>
    /// <param name="prevista">Linha da grade.</param>
    public static Parcela Nova(Guid vinculoId, Guid itemId, ParcelaPrevista prevista) =>
        new()
        {
            VinculoId = vinculoId,
            ItemDeCobrancaId = itemId,
            Numero = prevista.Numero,
            Vencimento = prevista.Vencimento,
            ValorOriginalEmCentavos = prevista.ValorEmCentavos,
        };

    /// <summary>A situação no dia: aberta com vencimento passado é vencida.</summary>
    /// <param name="hoje">Dia de referência.</param>
    public StatusDaParcela StatusEm(DateOnly hoje) => StatusNoDia(Status, Vencimento, hoje);

    /// <summary>A mesma regra de <see cref="StatusEm"/>, para quem leu só as colunas.</summary>
    /// <param name="gravado">Status gravado.</param>
    /// <param name="vencimento">Dia do vencimento.</param>
    /// <param name="hoje">Dia de referência.</param>
    public static StatusDaParcela StatusNoDia(StatusDaParcela gravado, DateOnly vencimento, DateOnly hoje) =>
        gravado == StatusDaParcela.Aberta && vencimento < hoje ? StatusDaParcela.Vencida : gravado;

    /// <summary>
    /// Aplica o valor novo do item, se a parcela ainda não venceu.
    /// </summary>
    /// <remarks>
    /// Paga, vencida, cancelada ou renegociada fica como está: a mudança do plano vale só para o
    /// que ainda não venceu. Vence hoje ainda não venceu.
    /// </remarks>
    /// <param name="valorEmCentavos">Valor da mesma posição na grade nova.</param>
    /// <param name="hoje">Dia de referência.</param>
    /// <returns>Se o valor mudou.</returns>
    public bool Repactuar(long valorEmCentavos, DateOnly hoje)
    {
        if (StatusEm(hoje) != StatusDaParcela.Aberta || ValorOriginalEmCentavos == valorEmCentavos)
            return false;

        ValorOriginalEmCentavos = valorEmCentavos;

        return true;
    }

    /// <summary>O valor da parcela no dia, pelas regras que o formando aceitou.</summary>
    /// <param name="dia">Dia do pagamento.</param>
    /// <param name="regras">Regras do snapshot da adesão.</param>
    public ValorDoDia ValorEm(DateOnly dia, RegrasDeAtraso regras) => ValorDoDia.Calcular(ValorOriginalEmCentavos, Vencimento, dia, regras);

    /// <summary>
    /// Registra o pagamento. Só a <c>BaixaService</c> chama — é a porta única da baixa.
    /// </summary>
    /// <remarks>Aberta ou vencida recebe; paga, cancelada ou renegociada, não.</remarks>
    /// <param name="valorEmCentavos">O que entrou na conta.</param>
    /// <param name="pagoEm">Dia em que entrou.</param>
    public Result Pagar(long valorEmCentavos, DateOnly pagoEm)
    {
        if (Status != StatusDaParcela.Aberta)
            return Result.Falha(Erro.Conflito("pagamento.parcela_nao_aberta", "Esta parcela não está em aberto."));

        Status = StatusDaParcela.Paga;
        ValorPagoEmCentavos = valorEmCentavos;
        PagoEm = pagoEm;

        return Result.Ok();
    }

    /// <summary>Desfaz a baixa: a parcela volta a ser devida, e o valor do dia volta a correr.</summary>
    public Result Estornar()
    {
        if (Status != StatusDaParcela.Paga)
            return Result.Falha(Erro.Conflito("pagamento.parcela_nao_paga", "Esta parcela não está paga."));

        Status = StatusDaParcela.Aberta;
        ValorPagoEmCentavos = null;
        PagoEm = null;

        return Result.Ok();
    }

    /// <summary>Deixa de ser devida, se ainda não venceu. As demais ficam como estão.</summary>
    /// <param name="hoje">Dia de referência.</param>
    /// <returns>Se foi cancelada.</returns>
    public bool Cancelar(DateOnly hoje)
    {
        if (StatusEm(hoje) != StatusDaParcela.Aberta)
            return false;

        Status = StatusDaParcela.Cancelada;

        return true;
    }
}
