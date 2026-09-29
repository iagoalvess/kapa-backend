namespace Backend.Business.Marketing.Settings;

/// <summary>
/// Liga o marketing do Kapa e diz onde fica o descadastro de um clique.
/// </summary>
/// <remarks>
/// O remetente (<c>novidades@novidades.kapaformaturas.com.br</c>, P4) mora em <c>Smtp</c>, com o
/// transacional: é identidade de envio, e quem a usa é o <c>SmtpEmailSender</c>.
/// </remarks>
public sealed class ComunicacaoDoKapaSettings
{
    /// <summary>Nome da seção correspondente no arquivo de configuração.</summary>
    public const string Secao = "ComunicacaoDoKapa";

    /// <summary>
    /// Se as jornadas mandam e-mail. Desligado por padrão.
    /// </summary>
    /// <remarks>
    /// A Política de Privacidade precisa dizer que o Kapa usa o e-mail da comissão para comunicação própria
    /// antes do primeiro envio (P9, com o advogado). O código fica pronto e a chave, desligada, até lá. A
    /// preferência, o descadastro e o histórico funcionam com ela desligada.
    /// </remarks>
    public bool EnvioLigado { get; init; }

    /// <summary>
    /// Endereço público da API, sem a barra final — de onde sai o <c>List-Unsubscribe</c>.
    /// </summary>
    /// <remarks>
    /// O cabeçalho da RFC 8058 manda o cliente de e-mail fazer um <c>POST</c> direto na URL, sem abrir tela:
    /// tem de ser a API, e não o app, que é estático.
    /// </remarks>
    public string UrlDaApi { get; init; } = "https://localhost:7104";

    /// <summary>Por quantos dias o link de descadastro vale.</summary>
    /// <remarks>
    /// Longo de propósito: o link sai num e-mail que pode ser aberto meses depois, e sair é um direito a
    /// qualquer tempo (art. 18, §2º). Vencido, "Minha privacidade" continua ali.
    /// </remarks>
    public int DiasDoLinkDeDescadastro { get; init; } = 365;
}
