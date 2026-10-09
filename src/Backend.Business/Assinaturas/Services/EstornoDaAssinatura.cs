using Backend.Business.Abstractions;
using Backend.Business.Admin.Models;
using Backend.Business.Assinaturas.Interfaces;
using Backend.Business.Assinaturas.Models;
using Backend.Business.Formaturas.Interfaces;
using Backend.Business.Formaturas.Models;
using Backend.Business.Pagamentos.Models;

namespace Backend.Business.Assinaturas.Services;

/// <summary>
/// A devolução de um pagamento do plano, com o fim da assinatura: o núcleo do estorno pelo suporte e da desistência
/// pelo Presidente.
/// </summary>
/// <remarks>
/// Quem chama abre a transação, grava a auditoria com o nome dela e salva. Aqui fica só o que é igual nas duas
/// portas: o prazo dos 7 dias, a ordem das chamadas ao provedor e o estado que fica depois.
/// </remarks>
/// <param name="assinaturaRepository">O plano, para o ciclo do proporcional.</param>
/// <param name="formaturaRepository">A turma, que fica só para consulta.</param>
/// <param name="provedor">O PSP da licença.</param>
public sealed class EstornoDaAssinatura(
    IAssinaturaRepository assinaturaRepository,
    IFormaturaRepository formaturaRepository,
    IProvedorDeAssinatura provedor
)
{
    /// <summary>A janela da desistência com reembolso integral: 7 dias do pagamento (Termos, seção 7; art. 49 do CDC).</summary>
    public const int DiasDeDesistencia = 7;

    /// <summary>Se o pagamento ainda está dentro da janela da desistência.</summary>
    /// <param name="pagaEm">Quando o pagamento foi confirmado, em UTC.</param>
    /// <param name="agoraUtc">Momento da conferência.</param>
    public static bool DentroDaDesistencia(DateTime pagaEm, DateTime agoraUtc) => pagaEm >= agoraUtc.AddDays(-DiasDeDesistencia);

    /// <summary>Até quando a desistência vale para um pagamento confirmado em <paramref name="pagaEm"/>.</summary>
    /// <param name="pagaEm">Quando o pagamento foi confirmado, em UTC.</param>
    public static DateTime DesistenciaAte(DateTime pagaEm) => pagaEm.AddDays(DiasDeDesistencia);

    /// <summary>Devolve o pagamento, cancela a renovação no provedor, encerra a assinatura e suspende a turma.</summary>
    /// <remarks>
    /// A ordem protege o dinheiro: a renovação é cancelada no provedor <b>antes</b> do estorno — se o provedor falhar
    /// ali, nada foi devolvido e nada muda. Depois do estorno, o que resta é gravar; o aviso de recorrência cancelada
    /// que o provedor manda em seguida acerta a assinatura se a gravação falhar.
    /// <para>
    /// O proporcional é o que falta da vigência sobre o ciclo do plano. <c>ponytail:</c> mede pela vigência atual —
    /// estornar um ciclo antigo pelo proporcional devolve o que falta do ciclo corrente; o suporte estorna o último.
    /// </para>
    /// </remarks>
    /// <param name="assinatura">A assinatura paga pela cobrança, rastreada.</param>
    /// <param name="cobranca">O pagamento a devolver, rastreado.</param>
    /// <param name="modo">Integral (dentro dos 7 dias) ou proporcional.</param>
    /// <param name="agoraUtc">Momento do estorno.</param>
    /// <param name="ct">Token de cancelamento.</param>
    /// <returns>Quanto voltou, em centavos.</returns>
    public async Task<Result<long>> Devolver(
        Assinatura assinatura,
        CobrancaDaAssinatura cobranca,
        ModoDeEstorno modo,
        DateTime agoraUtc,
        CancellationToken ct = default
    )
    {
        if (cobranca is not { Situacao: SituacaoDaCobrancaDoPlano.Paga, IdDoPagamento: { } idDoPagamento, PagaEm: { } pagaEm })
            return Erro.Conflito("estorno.cobranca_nao_paga", "Só um pagamento confirmado, e ainda não estornado, pode ser estornado.");

        if (modo == ModoDeEstorno.Integral && !DentroDaDesistencia(pagaEm, agoraUtc))
            return Erro.Conflito(
                "estorno.fora_do_prazo",
                "O reembolso integral vale até 7 dias depois do pagamento. Depois disso, só o proporcional, nos casos dos Termos."
            );

        var ciclo = (await assinaturaRepository.ObterPlano(assinatura.PlanoId, ct))?.Ciclo ?? CicloDeCobranca.Mensal;
        var valor =
            modo == ModoDeEstorno.Integral
                ? cobranca.ValorEmCentavos
                : (long)Math.Round(cobranca.ValorEmCentavos * assinatura.FracaoRestante(agoraUtc, ciclo));

        if (valor <= 0)
            return Erro.Conflito("estorno.nada_a_devolver", "A vigência deste pagamento já acabou: não há o que devolver pelo proporcional.");

        if (assinatura.IdExterno is { } recorrencia)
        {
            var cancelada = await provedor.Cancelar(recorrencia, ct);
            if (cancelada.Falhou)
                return Result.Falha<long>(cancelada.Erros);
        }

        var devolvido = await provedor.Estornar(idDoPagamento, valor, cobranca.Id, ct);
        if (devolvido.Falhou)
            return Result.Falha<long>(devolvido.Erros);

        cobranca.Estornar(valor, agoraUtc);
        assinatura.IdExterno = null;
        assinatura.Encerrar(agoraUtc);

        var formatura = await formaturaRepository.ObterParaEdicao(assinatura.FormaturaId, ct);
        formatura?.Transicionar(StatusDaFormatura.Suspensa);

        return valor;
    }
}
