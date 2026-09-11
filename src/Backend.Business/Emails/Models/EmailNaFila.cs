using Backend.Business.Abstractions;

namespace Backend.Business.Emails.Models;

/// <summary>Situação de um e-mail na fila.</summary>
public enum EEmailStatus
{
    /// <summary>Aguardando envio.</summary>
    Pendente = 0,

    /// <summary>Reservado por um worker e em processo de envio.</summary>
    Enviando = 1,

    /// <summary>Aceito pelo servidor de e-mail.</summary>
    Enviado = 2,

    /// <summary>Esgotou as tentativas. Não será mais tentado automaticamente.</summary>
    Falhou = 3,
}

/// <summary>Ordem de atendimento na fila.</summary>
/// <remarks>
/// Serve para um e-mail de redefinição de senha, que o usuário está esperando na tela, não ficar
/// atrás de dez mil mensagens de um disparo em massa.
/// </remarks>
public enum EEmailPrioridade
{
    /// <summary>Disparo em massa, relatório, aviso.</summary>
    Normal = 0,

    /// <summary>O usuário está esperando: confirmação de conta, redefinição de senha.</summary>
    Alta = 1,
}

/// <summary>
/// Um e-mail aguardando envio.
/// </summary>
/// <remarks>
/// A entidade é dona das próprias transições de estado (<see cref="MarcarEmEnvio"/>,
/// <see cref="MarcarEnviado"/>, <see cref="RegistrarFalha"/>). O job só orquestra. É o que
/// permite testar a política de retentativa sem SMTP, sem banco e sem worker.
/// </remarks>
public class EmailNaFila : Entity
{
    /// <summary>Destinatário.</summary>
    public string Para { get; set; } = string.Empty;

    /// <summary>Assunto da mensagem.</summary>
    public string Assunto { get; set; } = string.Empty;

    /// <summary>Corpo em HTML.</summary>
    public string CorpoHtml { get; set; } = string.Empty;

    /// <summary>Situação atual.</summary>
    public EEmailStatus Status { get; set; } = EEmailStatus.Pendente;

    /// <summary>Ordem de atendimento.</summary>
    public EEmailPrioridade Prioridade { get; set; } = EEmailPrioridade.Normal;

    /// <summary>Quantas vezes o envio já foi tentado.</summary>
    public int Tentativas { get; set; }

    /// <summary>A partir de quando o e-mail pode ser tentado, em UTC.</summary>
    public DateTime ProximaTentativaEm { get; set; } = DateTime.UtcNow;

    /// <summary>Mensagem da última falha, para diagnóstico.</summary>
    public string? UltimoErro { get; set; }

    /// <summary>Momento do envio aceito, em UTC.</summary>
    public DateTime? EnviadoEm { get; set; }

    /// <summary>Marca o e-mail como reservado por um worker.</summary>
    /// <param name="agoraUtc">Momento da reserva.</param>
    public void MarcarEmEnvio(DateTime agoraUtc)
    {
        Status = EEmailStatus.Enviando;
        Tentativas++;
        AtualizadoEm = agoraUtc;
    }

    /// <summary>Marca o e-mail como aceito pelo servidor.</summary>
    /// <param name="agoraUtc">Momento do aceite.</param>
    public void MarcarEnviado(DateTime agoraUtc)
    {
        Status = EEmailStatus.Enviado;
        EnviadoEm = agoraUtc;
        UltimoErro = null;
        AtualizadoEm = agoraUtc;
    }

    /// <summary>
    /// Registra uma falha de envio e agenda a próxima tentativa.
    /// </summary>
    /// <remarks>
    /// A espera cresce exponencialmente (3, 9, 27, 81 minutos). Retentar de imediato contra um
    /// servidor que está recusando só acelera o bloqueio por reputação — e, se a causa for uma
    /// indisponibilidade momentânea, ela raramente se resolve no mesmo segundo.
    /// <para>
    /// Esgotadas as tentativas, o registro fica em <see cref="EEmailStatus.Falhou"/> com o erro
    /// preservado. Não é apagado: um e-mail que não chegou é justamente o que alguém vai
    /// procurar depois.
    /// </para>
    /// </remarks>
    /// <param name="erro">Descrição da falha.</param>
    /// <param name="agoraUtc">Momento da falha.</param>
    /// <param name="maximoDeTentativas">Tentativas permitidas antes de desistir.</param>
    public void RegistrarFalha(string erro, DateTime agoraUtc, int maximoDeTentativas)
    {
        UltimoErro = erro.Length > 1000 ? erro[..1000] : erro;
        AtualizadoEm = agoraUtc;

        if (Tentativas >= maximoDeTentativas)
        {
            Status = EEmailStatus.Falhou;
            return;
        }

        Status = EEmailStatus.Pendente;
        ProximaTentativaEm = agoraUtc.AddMinutes(Math.Pow(3, Tentativas));
    }
}
