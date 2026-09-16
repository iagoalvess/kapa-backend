namespace Backend.Business.Notificacoes.Models;

/// <summary>
/// O assunto de uma mensagem automática — e o que o formando pode desligar.
/// </summary>
/// <remarks>
/// <see cref="Cobranca"/> é comunicação contratual, prevista no termo de adesão (Sprint 7): não se
/// desliga (decisão 7). Os demais são conveniência e o titular escolhe.
/// <para>Gravado como texto, pelo mesmo motivo de <c>StatusDaFormatura</c>.</para>
/// </remarks>
public enum TipoDeNotificacao
{
    /// <summary>Parcela a vencer, vencida, ou o resumo à tesouraria. Obrigatória.</summary>
    Cobranca,

    /// <summary>Aviso do mural e lembrete de assembleia. Opcional.</summary>
    Aviso,

    /// <summary>Termo de adesão publicado, a assinar. Opcional.</summary>
    Adesao,

    /// <summary>Recado da plataforma. Opcional.</summary>
    Sistema,
}

/// <summary>Quais tipos o titular pode desligar.</summary>
public static class TiposDeNotificacao
{
    /// <summary>Se o formando pode desligar este tipo.</summary>
    /// <param name="tipo">Tipo pretendido.</param>
    public static bool Opcional(TipoDeNotificacao tipo) => tipo is not TipoDeNotificacao.Cobranca;
}
