using Backend.Business.Abstractions;

namespace Backend.Business.Notificacoes.Models;

/// <summary>
/// O que um membro escolheu não receber.
/// </summary>
/// <remarks>
/// Uma linha por <c>(VinculoId, Tipo)</c>, com índice único: a ausência de linha é "recebe", que é o
/// padrão. Só o que <c>TiposDeNotificacao.Opcional</c> permite chega a virar linha — cobrança de
/// parcela é comunicação contratual do termo de adesão e não se desliga (decisão 7).
/// </remarks>
public class PreferenciaDeNotificacao : EntidadeDaFormatura
{
    /// <summary>Vínculo do titular.</summary>
    public Guid VinculoId { get; private set; }

    /// <summary>Assunto que a preferência governa.</summary>
    public TipoDeNotificacao Tipo { get; private set; }

    /// <summary>Se o titular quer receber.</summary>
    public bool Ativa { get; private set; } = true;

    /// <summary>A escolha de um titular para um tipo.</summary>
    /// <param name="vinculoId">Vínculo do titular.</param>
    /// <param name="tipo">Assunto.</param>
    /// <param name="ativa">Se quer receber.</param>
    public static PreferenciaDeNotificacao Nova(Guid vinculoId, TipoDeNotificacao tipo, bool ativa) =>
        new()
        {
            VinculoId = vinculoId,
            Tipo = tipo,
            Ativa = ativa,
        };

    /// <summary>Troca a escolha.</summary>
    /// <param name="ativa">Se quer receber.</param>
    public void Definir(bool ativa) => Ativa = ativa;
}
