namespace Backend.Business.Emails.Settings;

/// <summary>
/// Conexão com o servidor de e-mail.
/// </summary>
/// <remarks>
/// SMTP, e não o SDK de um provedor específico, porque **todos** falam SMTP — Amazon SES,
/// SendGrid, Mailgun, Resend, Postmark, Gmail. Trocar de provedor é mudar host, porta e
/// credencial no ambiente, sem recompilar e sem tocar em código.
/// <para>
/// Um cliente específico de provedor só passa a valer a pena quando você precisa de algo que
/// SMTP não oferece: webhook de bounce, template hospedado no provedor ou envio em lote com
/// personalização. Nesse dia, implemente <c>IEmailSender</c> e troque o registro na injeção de
/// dependência — nada mais no projeto muda.
/// </para>
/// <para>
/// Com <see cref="Host"/> vazio, a aplicação registra um remetente que apenas escreve a mensagem
/// no log. É o que faz <c>docker compose up</c> funcionar sem um servidor de e-mail à mão.
/// </para>
/// </remarks>
public sealed class SmtpSettings
{
    /// <summary>Nome da seção correspondente no arquivo de configuração.</summary>
    public const string Secao = "Smtp";

    /// <summary>Servidor SMTP. Vazio ativa o remetente de log.</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>Porta. 587 para STARTTLS, 465 para SSL implícito.</summary>
    public int Porta { get; init; } = 587;

    /// <summary>Usuário de autenticação. Vazio envia sem autenticar.</summary>
    public string Usuario { get; init; } = string.Empty;

    /// <summary>Senha de autenticação.</summary>
    public string Senha { get; init; } = string.Empty;

    /// <summary>Endereço que aparece como remetente.</summary>
    public string RemetenteEmail { get; init; } = string.Empty;

    /// <summary>Nome de exibição do remetente.</summary>
    public string RemetenteNome { get; init; } = string.Empty;

    /// <summary>
    /// Caixa que recebe <b>tudo</b> no lugar do destinatário real. Vazio, cada um recebe o seu.
    /// </summary>
    /// <remarks>
    /// Para desenvolver: a régua de cobrança de uma turma de teste dispara dezenas de mensagens para
    /// endereços inventados, e sem isto não há como ler nenhuma delas. O destinatário original vai no
    /// assunto e no log, para a mensagem continuar identificável.
    /// <para>
    /// <b>Nunca preencha em produção.</b> Mora em <c>appsettings.Development.json</c>, que não é
    /// carregado lá; se algum dia aparecer preenchido num ambiente real, todo e-mail da plataforma
    /// para de chegar a quem devia — e o log diz, a cada envio, para onde foi.
    /// </para>
    /// </remarks>
    public string RedirecionarPara { get; init; } = string.Empty;

    /// <summary>Quantos e-mails o worker reserva por rodada.</summary>
    public int TamanhoDoLote { get; init; } = 20;

    /// <summary>Tentativas antes de desistir de um e-mail.</summary>
    public int MaximoDeTentativas { get; init; } = 5;

    /// <summary>Indica se a configuração aponta para um servidor real.</summary>
    public bool Configurado => !string.IsNullOrWhiteSpace(Host);
}
