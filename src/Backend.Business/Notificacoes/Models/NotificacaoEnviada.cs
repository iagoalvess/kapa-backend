using Backend.Business.Abstractions;

namespace Backend.Business.Notificacoes.Models;

/// <summary>O que aconteceu com uma mensagem depois que a régua a soltou.</summary>
/// <remarks>Gravado como texto: o histórico filtra por ele.</remarks>
public enum StatusDaNotificacao
{
    /// <summary>Entregue ao canal — para o e-mail, está na fila do worker.</summary>
    Enfileirada,

    /// <summary>O canal aceitou a mensagem.</summary>
    Entregue,

    /// <summary>O canal desistiu. Para o e-mail, o endereço passa a ser tratado como inválido.</summary>
    Falhou,
}

/// <summary>
/// Uma mensagem que a régua já disparou. É o registro que impede a segunda.
/// </summary>
/// <remarks>
/// <b>O requisito mais importante da sprint</b> (decisão 2): o índice único
/// <c>(ParcelaId, RegraId, DataDeReferencia)</c> — com <c>NULLS NOT DISTINCT</c>, porque o resumo à
/// tesouraria não tem parcela — faz o job rodar duas vezes, a instância duplicar e o worker
/// reiniciar no meio sem que ninguém receba a mesma cobrança duas vezes. Régua que manda duas vezes
/// é pior que régua nenhuma.
/// <para>
/// A linha é gravada na <b>mesma transação</b> do enfileiramento: ou a mensagem entra na fila e o
/// registro existe, ou nenhum dos dois.
/// </para>
/// </remarks>
public class NotificacaoEnviada : EntidadeDaFormatura
{
    /// <summary>Parcela cobrada. Nula no resumo à tesouraria, que não tem uma.</summary>
    public Guid? ParcelaId { get; private set; }

    /// <summary>Degrau da régua que disparou.</summary>
    public Guid RegraId { get; private set; }

    /// <summary>Dia (em Brasília) em que a régua rodou. É a terceira parte da chave.</summary>
    public DateOnly DataDeReferencia { get; private set; }

    /// <summary>Por onde saiu.</summary>
    public CanalDeNotificacao Canal { get; private set; }

    /// <summary>Para quem — o endereço, que é o que a tesouraria mostra quando alguém diz que não foi avisado.</summary>
    public string Destinatario { get; private set; } = string.Empty;

    /// <summary>Vínculo de quem recebeu. Nulo quando o destinatário é a tesouraria.</summary>
    public Guid? VinculoId { get; private set; }

    /// <summary>Assunto enviado, já com as variáveis trocadas.</summary>
    public string Assunto { get; private set; } = string.Empty;

    /// <summary>Situação da entrega.</summary>
    public StatusDaNotificacao Status { get; private set; } = StatusDaNotificacao.Enfileirada;

    /// <summary>O e-mail correspondente na fila — é por ele que a entrega é conferida depois.</summary>
    public Guid? EmailNaFilaId { get; private set; }

    /// <summary>Por que o canal desistiu.</summary>
    public string? Erro { get; private set; }

    /// <summary>Registra o disparo de uma mensagem.</summary>
    /// <param name="regraId">Degrau que disparou.</param>
    /// <param name="dia">Dia de referência, em Brasília.</param>
    /// <param name="canal">Por onde saiu.</param>
    /// <param name="destinatario">Endereço.</param>
    /// <param name="assunto">Assunto já renderizado.</param>
    /// <param name="parcelaId">Parcela cobrada, se houver.</param>
    /// <param name="vinculoId">Vínculo de quem recebeu, se houver.</param>
    /// <param name="emailNaFilaId">E-mail correspondente na fila, se o canal foi o e-mail.</param>
    public static NotificacaoEnviada Nova(
        Guid regraId,
        DateOnly dia,
        CanalDeNotificacao canal,
        string destinatario,
        string assunto,
        Guid? parcelaId = null,
        Guid? vinculoId = null,
        Guid? emailNaFilaId = null
    ) =>
        new()
        {
            RegraId = regraId,
            DataDeReferencia = dia,
            Canal = canal,
            Destinatario = destinatario,
            Assunto = assunto,
            ParcelaId = parcelaId,
            VinculoId = vinculoId,
            EmailNaFilaId = emailNaFilaId,
        };

    /// <summary>O canal aceitou.</summary>
    public void MarcarEntregue() => Status = StatusDaNotificacao.Entregue;

    /// <summary>O canal desistiu — e o endereço passa a ser pulado pela régua.</summary>
    /// <param name="erro">Última mensagem de erro do canal.</param>
    public void MarcarFalha(string? erro)
    {
        Status = StatusDaNotificacao.Falhou;
        Erro = erro is { Length: > 500 } ? erro[..500] : erro;
    }
}
