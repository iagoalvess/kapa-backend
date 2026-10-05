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

    /// <summary>
    /// Quanto já entrou na conta da turma por esta parcela, em centavos. Soma das baixas não estornadas.
    /// </summary>
    /// <remarks>
    /// Acumulado desde 17/09/2026: antes, qualquer valor fechava a parcela, e quem pagava R$ 200 de
    /// R$ 350 saía do "a receber" devendo R$ 150 que só apareciam na aba Divergências. Pagar o que dá
    /// é o normal numa formatura, não a exceção.
    /// <para>
    /// Sobra acima do devido continua indo para Divergências, não para um saldo (decisão 5 da Sprint 9).
    /// </para>
    /// </remarks>
    public long? ValorPagoEmCentavos { get; private set; }

    /// <summary>Dia em que a parcela foi quitada. Só quando o pago alcançou o devido.</summary>
    public DateOnly? PagoEm { get; private set; }

    /// <summary>
    /// Até quando a parcela está fora da régua e da inadimplência — o prazo de resposta de uma solicitação de
    /// cancelamento (Sprint 48, D12/D37). Nulo, ou no passado: cobra normalmente.
    /// </summary>
    /// <remarks>
    /// Data, e não marca: vencido o prazo sem resposta, a cobrança volta sozinha, sem job. É coluna, e não
    /// subconsulta na solicitação, porque as agregações por situação (<c>GROUP BY</c>) não traduzem subconsulta.
    /// </remarks>
    public DateOnly? SuspensaAte { get; private set; }

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
    /// <remarks>
    /// Na leitura, a suspensa continua aberta (D12): a tela não pode chamar de vencida o que a régua e a
    /// inadimplência deixaram de contar. Quem decide (repactuar, cancelar) usa <see cref="StatusEm"/>, sem ela.
    /// </remarks>
    /// <param name="gravado">Status gravado.</param>
    /// <param name="vencimento">Dia do vencimento.</param>
    /// <param name="hoje">Dia de referência.</param>
    /// <param name="suspensaAte">Até quando a parcela está suspensa, se estiver.</param>
    public static StatusDaParcela StatusNoDia(StatusDaParcela gravado, DateOnly vencimento, DateOnly hoje, DateOnly? suspensaAte = null) =>
        gravado == StatusDaParcela.Aberta && vencimento < hoje && !(suspensaAte >= hoje) ? StatusDaParcela.Vencida : gravado;

    /// <summary>Tira a parcela aberta da régua e da inadimplência até o dia informado (D12). Paga ou cancelada não muda.</summary>
    /// <param name="ate">Último dia da suspensão — o prazo de resposta da comissão.</param>
    public void Suspender(DateOnly ate)
    {
        if (Status == StatusDaParcela.Aberta)
            SuspensaAte = ate;
    }

    /// <summary>A comissão respondeu: a cobrança volta no mesmo dia.</summary>
    public void Retomar() => SuspensaAte = null;

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

    /// <summary>O valor da parcela no dia, pelas regras que o formando aceitou, já abatido o que ele pagou.</summary>
    /// <param name="dia">Dia do pagamento.</param>
    /// <param name="regras">Regras do snapshot da adesão.</param>
    public ValorDoDia ValorEm(DateOnly dia, RegrasDeAtraso regras) =>
        ValorDoDia.Calcular(ValorOriginalEmCentavos, Vencimento, dia, regras, ValorPagoEmCentavos ?? 0);

    /// <summary>
    /// Registra um pagamento. Só a <c>BaixaService</c> chama — é a porta única da baixa.
    /// </summary>
    /// <remarks>
    /// O valor <b>soma</b> ao que já entrou, e a parcela só fecha quando o total alcança o
    /// <see cref="QuitaCom"/>: pagar R$ 200 de R$ 350 deixa a parcela em aberto com R$ 150 a receber,
    /// que é o que a pessoa de fato ainda deve. Aberta ou vencida recebe; paga, cancelada ou
    /// renegociada, não.
    /// </remarks>
    /// <param name="valorEmCentavos">O que entrou na conta agora.</param>
    /// <param name="pagoEm">Dia em que entrou.</param>
    /// <param name="devidoEmCentavos">O valor do dia, com encargos ou desconto, <b>sem</b> abater o já pago.</param>
    /// <returns>Se a parcela ficou quitada.</returns>
    public Result<bool> Pagar(long valorEmCentavos, DateOnly pagoEm, long devidoEmCentavos)
    {
        if (Status != StatusDaParcela.Aberta)
            return Result.Falha<bool>(Erro.Conflito("pagamento.parcela_nao_aberta", "Esta parcela não está em aberto."));

        ValorPagoEmCentavos = (ValorPagoEmCentavos ?? 0) + valorEmCentavos;

        if (ValorPagoEmCentavos < QuitaCom(devidoEmCentavos))
            return false;

        Status = StatusDaParcela.Paga;
        PagoEm = pagoEm;

        return true;
    }

    /// <summary>
    /// Quanto fecha a parcela: o menor entre o devido do dia e o valor original.
    /// </summary>
    /// <remarks>
    /// O devido sozinho não serve: quem paga adiantado deve menos que o original, e exigir o original
    /// deixaria a parcela aberta por um desconto que a própria turma deu. O original sozinho também
    /// não: quem paga atrasado deve mais.
    /// <para>
    /// O menor dos dois é a regra que a prática pede — pagar o principal encerra a parcela, e a multa
    /// e os juros não pagos viram divergência, que é onde a tesouraria decide se cobra ou perdoa. Sem
    /// isso, a parcela do formando que pagou tudo menos R$ 10,50 de juros ficaria em aberto para
    /// sempre, com a régua cobrando.
    /// </para>
    /// </remarks>
    /// <param name="devidoEmCentavos">O valor do dia, com encargos ou desconto.</param>
    public long QuitaCom(long devidoEmCentavos) => Math.Min(devidoEmCentavos, ValorOriginalEmCentavos);

    /// <summary>
    /// Desfaz uma baixa: o valor dela sai do pago, e a parcela volta a ser devida.
    /// </summary>
    /// <remarks>
    /// Recebe o valor porque a parcela pode ter várias baixas — o estorno é de <b>uma</b> delas. Uma
    /// parcela em aberto com pagamento parcial também estorna: não estava paga, mas o dinheiro entrou.
    /// <para>
    /// A cancelada com pagamento parcial também (Sprint 42, F2): o Mercado Pago pode devolver o que
    /// entrou nela, e recusar derrubava a transação inteira da devolução. Ela continua cancelada — só
    /// o pago diminui, e quem chama abate o mesmo valor do que a comissão tinha a devolver.
    /// </para>
    /// </remarks>
    /// <param name="valorEmCentavos">O valor da baixa estornada.</param>
    public Result Estornar(long valorEmCentavos)
    {
        if (Status is not (StatusDaParcela.Paga or StatusDaParcela.Aberta or StatusDaParcela.Cancelada) || ValorPagoEmCentavos is null)
            return Result.Falha(Erro.Conflito("pagamento.parcela_nao_paga", "Esta parcela não tem pagamento a estornar."));

        var restante = ValorPagoEmCentavos.Value - valorEmCentavos;

        Status = Status == StatusDaParcela.Cancelada ? StatusDaParcela.Cancelada : StatusDaParcela.Aberta;
        ValorPagoEmCentavos = restante > 0 ? restante : null;
        PagoEm = null;

        return Result.Ok();
    }

    /// <summary>
    /// Deixa de ser devida, se ainda não venceu. As demais ficam como estão.
    /// </summary>
    /// <remarks>
    /// Paga, cancelada e renegociada nunca cancelam. Vencida só com <paramref name="incluirVencidas"/>,
    /// e o único lugar que o pede é o desligamento do formando, onde a comissão vê a soma em atraso e
    /// decide (P1 de 17/09/2026). Encerrar item do plano continua poupando o atraso: lá a dívida
    /// vencida é de todo mundo, e ninguém decidiu perdoá-la.
    /// </remarks>
    /// <param name="hoje">Dia de referência.</param>
    /// <param name="incluirVencidas">Cancela também o que já venceu e não foi pago.</param>
    /// <returns>Se foi cancelada.</returns>
    public bool Cancelar(DateOnly hoje, bool incluirVencidas = false)
    {
        var situacao = StatusEm(hoje);

        if (situacao != StatusDaParcela.Aberta && !(incluirVencidas && situacao == StatusDaParcela.Vencida))
            return false;

        Status = StatusDaParcela.Cancelada;

        return true;
    }

    /// <summary>
    /// Deixa de ser devida em qualquer situação, inclusive paga: a comissão aprovou o cancelamento do pacote (D8).
    /// </summary>
    /// <remarks>
    /// O que já entrou continua no <see cref="ValorPagoEmCentavos"/>, e quem chama o leva à lista "a devolver" (D9) —
    /// é a mesma forma da cancelada com pagamento parcial (Sprint 42, F2), e o estorno dela já funciona.
    /// </remarks>
    /// <returns>Se mudou agora.</returns>
    public bool Desfazer()
    {
        if (Status == StatusDaParcela.Cancelada)
            return false;

        Status = StatusDaParcela.Cancelada;
        SuspensaAte = null;

        return true;
    }
}
