using Backend.Business.Abstractions;

namespace Backend.Business.Notificacoes.Models;

/// <summary>
/// O que faz uma régua disparar.
/// </summary>
/// <remarks>Gravado como texto: número mudaria de sentido no dia em que alguém reordenasse o enum.</remarks>
public enum GatilhoDaRegua
{
    /// <summary>
    /// Uma parcela cujo vencimento está a <c>DiasDeDeslocamento</c> dias de hoje.
    /// </summary>
    /// <remarks>
    /// Negativo é antes (D-5, lembrete), zero é o dia, positivo é atraso (D+3, D+15, D+30). O
    /// seletor compara <c>Vencimento = hoje − deslocamento</c>, sempre com o dia de Brasília.
    /// </remarks>
    Vencimento,

    /// <summary>
    /// Informe de pagamento parado na fila há <c>DiasDeDeslocamento</c> dias.
    /// </summary>
    /// <remarks>
    /// O único gatilho que não olha parcela: o destinatário é a tesouraria, e a mensagem é o resumo
    /// de quantos pagamentos esperam conferência. Informe parado é problema de quem confere, não de
    /// quem pagou — e é ele que trava a régua toda (risco da sprint).
    /// </remarks>
    InformePendente,
}

/// <summary>
/// Um degrau da régua da turma: qual é e se está ligado.
/// </summary>
/// <remarks>
/// O texto e quem recebe vêm de <see cref="ReguaDoKapa"/>, iguais para toda turma; a turma só liga ou
/// desliga. A turma nasce com todos ligados (decisão 5): exigir configuração antes de funcionar
/// significa que metade das turmas nunca terá lembrete.
/// <para>
/// O par <c>(Gatilho, DiasDeDeslocamento)</c> tem índice único por formatura — é a identidade do
/// degrau, e é por ele que se acha o texto no catálogo.
/// </para>
/// </remarks>
public class RegraDeNotificacao : EntidadeDaFormatura
{
    /// <summary>O que dispara.</summary>
    public GatilhoDaRegua Gatilho { get; private set; }

    /// <summary>Dias de distância do gatilho. Negativo é antes do vencimento.</summary>
    public int DiasDeDeslocamento { get; private set; }

    /// <summary>Se o degrau dispara. Desligado, a régua o pula sem gravar nada.</summary>
    public bool Ativa { get; private set; } = true;

    /// <summary>Um degrau novo, ligado.</summary>
    /// <param name="degrau">Degrau do catálogo.</param>
    public static RegraDeNotificacao Nova(DegrauDaRegua degrau) => new() { Gatilho = degrau.Gatilho, DiasDeDeslocamento = degrau.DiasDeDeslocamento };

    /// <summary>Liga ou desliga o degrau.</summary>
    /// <param name="ativa">Se dispara.</param>
    public void Definir(bool ativa) => Ativa = ativa;
}
