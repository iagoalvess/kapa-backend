using Backend.Business.Abstractions;

namespace Backend.Business.Assinaturas.Models;

/// <summary>
/// Contratação da licença por uma formatura.
/// </summary>
/// <remarks>
/// Uma linha por contratação: a turma suspensa que volta a pagar ganha uma assinatura nova, e a
/// vencida fica como histórico. A "assinatura da formatura" é sempre a mais recente.
/// <para>
/// Status e vigência mudam só pelos métodos daqui, pelo mesmo motivo de
/// <c>Formatura.Transicionar</c>: as regras de quem vira o quê ficam num lugar só, testáveis sem
/// banco, sem provedor e sem worker.
/// </para>
/// </remarks>
public class Assinatura : EntidadeDaFormatura
{
    /// <summary>Marcos dos avisos de vencimento, em dias até a vigência acabar. Negativo é depois.</summary>
    public static readonly IReadOnlyList<int> MarcosDeAviso = [7, 3, -1];

    /// <summary>Plano contratado.</summary>
    public Guid PlanoId { get; set; }

    /// <summary>Situação. Muda só pelos métodos da entidade.</summary>
    public StatusDaAssinatura Status { get; private set; } = StatusDaAssinatura.Pendente;

    /// <summary>Identificador da assinatura (ou da sessão de checkout) no provedor.</summary>
    public string? IdExterno { get; set; }

    /// <summary>Até quando a licença paga vale, em UTC. Nulo enquanto não houver pagamento.</summary>
    public DateTime? VigenteAte { get; private set; }

    /// <summary>Quando a renovação foi cancelada, em UTC.</summary>
    public DateTime? CanceladaEm { get; private set; }

    /// <summary>Último marco de aviso de vencimento enviado. Ver <see cref="MarcosDeAviso"/>.</summary>
    public int? UltimoAvisoDeVencimento { get; private set; }

    /// <summary>Primeiro pagamento confirmado: a assinatura passa a valer por um ciclo a partir de agora.</summary>
    /// <remarks>
    /// Só sai de <see cref="StatusDaAssinatura.Pendente"/>. É o que torna a confirmação idempotente
    /// mesmo quando chega duas vezes com ids diferentes — a conciliação achou o pagamento e depois o
    /// webhook atrasado chegou: a segunda não soma outro ciclo.
    /// </remarks>
    /// <param name="agoraUtc">Momento da confirmação.</param>
    /// <param name="ciclo">Ciclo do plano.</param>
    public Result ConfirmarPagamento(DateTime agoraUtc, CicloDeCobranca ciclo)
    {
        if (Status != StatusDaAssinatura.Pendente)
            return Invalida(StatusDaAssinatura.Ativa);

        Status = StatusDaAssinatura.Ativa;
        VigenteAte = ciclo.Somar(agoraUtc);
        UltimoAvisoDeVencimento = null;

        return Result.Ok();
    }

    /// <summary>Renovação cobrada pelo provedor: soma um ciclo à vigência.</summary>
    /// <remarks>
    /// Soma a partir do fim da vigência atual, e não de agora: quem renova antes do vencimento não
    /// perde os dias que faltavam. Vencida volta a valer a partir de agora.
    /// </remarks>
    /// <param name="agoraUtc">Momento da renovação.</param>
    /// <param name="ciclo">Ciclo do plano.</param>
    public Result Renovar(DateTime agoraUtc, CicloDeCobranca ciclo)
    {
        if (Status is not (StatusDaAssinatura.Ativa or StatusDaAssinatura.Vencida))
            return Invalida(StatusDaAssinatura.Ativa);

        var inicio = VigenteAte is { } ate && ate > agoraUtc ? ate : agoraUtc;

        Status = StatusDaAssinatura.Ativa;
        VigenteAte = ciclo.Somar(inicio);
        UltimoAvisoDeVencimento = null;

        return Result.Ok();
    }

    /// <summary>Cancela a renovação. A vigência corrente continua valendo.</summary>
    /// <param name="agoraUtc">Momento do cancelamento.</param>
    public Result Cancelar(DateTime agoraUtc)
    {
        if (Status != StatusDaAssinatura.Ativa)
            return Result.Falha(Erro.Conflito("assinatura.nao_ativa", "Só uma assinatura ativa pode ser cancelada."));

        Status = StatusDaAssinatura.Cancelada;
        CanceladaEm = agoraUtc;

        return Result.Ok();
    }

    /// <summary>Encerra a vigência. Quem chama suspende a formatura.</summary>
    public Result Vencer()
    {
        if (Status is not (StatusDaAssinatura.Ativa or StatusDaAssinatura.Cancelada))
            return Invalida(StatusDaAssinatura.Vencida);

        Status = StatusDaAssinatura.Vencida;

        return Result.Ok();
    }

    /// <summary>Se a vigência (mais a carência, para quem ia renovar) já acabou.</summary>
    /// <remarks>
    /// A carência vale só para a <see cref="StatusDaAssinatura.Ativa"/>: falha de cartão se resolve
    /// em dois dias, e suspender no minuto seguinte gera mais suporte que receita. Quem cancelou não
    /// tem cobrança por vir — a turma vira leitura no fim da vigência, como o diálogo prometeu.
    /// </remarks>
    /// <param name="agoraUtc">Momento da verificação.</param>
    /// <param name="carencia">Tolerância depois do vencimento.</param>
    public bool DeveVencer(DateTime agoraUtc, TimeSpan carencia) =>
        VigenteAte is { } ate
        && Status switch
        {
            StatusDaAssinatura.Ativa => ate + carencia <= agoraUtc,
            StatusDaAssinatura.Cancelada => ate <= agoraUtc,
            _ => false,
        };

    /// <summary>O marco de aviso a enviar agora, ou nulo se não houver aviso pendente.</summary>
    /// <remarks>
    /// Devolve só o marco mais avançado já alcançado: com o worker parado por dois dias, sai um aviso
    /// (o atual), não três de uma vez.
    /// </remarks>
    /// <param name="agoraUtc">Momento da verificação.</param>
    public int? AvisoDeVencimentoDevido(DateTime agoraUtc)
    {
        if (Status is not (StatusDaAssinatura.Ativa or StatusDaAssinatura.Cancelada) || VigenteAte is not { } ate)
            return null;

        int? alcancado = null;

        foreach (var marco in MarcosDeAviso.Where(marco => agoraUtc >= ate.AddDays(-marco)))
            alcancado = marco;

        return alcancado is { } devido && (UltimoAvisoDeVencimento is null || devido < UltimoAvisoDeVencimento) ? devido : null;
    }

    /// <summary>Registra que o aviso do marco foi enfileirado.</summary>
    /// <param name="marco">Marco enviado.</param>
    public void RegistrarAviso(int marco) => UltimoAvisoDeVencimento = marco;

    private Result Invalida(StatusDaAssinatura destino) =>
        Result.Falha(Erro.Conflito("assinatura.transicao_invalida", $"Uma assinatura {Status} não pode passar para {destino}."));
}
