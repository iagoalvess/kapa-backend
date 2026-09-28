namespace Backend.Business.MercadoPago.Settings;

/// <summary>
/// Seção <c>MercadoPago</c>: a aplicação do Kapa no Mercado Pago e onde fica a API.
/// </summary>
/// <remarks>
/// São as credenciais da <b>aplicação</b>, não de uma conta: com elas o Kapa pede autorização às turmas
/// (OAuth) e confere a assinatura dos avisos de pagamento. O token de cada turma mora cifrado no banco.
/// O sandbox usa a mesma URL — quem decide se a cobrança é de teste é o token, de um usuário de teste.
/// <para>
/// <see cref="ClientId"/> vazio desliga tudo: sem aplicação não há como conectar turma, e o formando
/// continua com os meios da Sprint 18.
/// </para>
/// </remarks>
public sealed class MercadoPagoSettings
{
    /// <summary>Nome da seção na configuração.</summary>
    public const string Secao = "MercadoPago";

    /// <summary>Raiz da API, terminada em barra.</summary>
    public string BaseUrl { get; init; } = "https://api.mercadopago.com/";

    /// <summary>
    /// Página de autorização do OAuth — a brasileira. A <c>auth.mercadopago.com</c> da documentação abre um
    /// seletor de país antes do login (conferido em 24/09/2026).
    /// </summary>
    public string UrlDeAutorizacao { get; init; } = "https://auth.mercadopago.com.br/authorization";

    /// <summary>O número da aplicação do Kapa (<c>client_id</c>).</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>O segredo da aplicação (<c>client_secret</c>). Também assina o <c>state</c> do OAuth.</summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// A URL de retorno do OAuth, cadastrada na aplicação — um endpoint da API, público e <c>https</c>.
    /// </summary>
    public string UrlDeRetorno { get; init; } = string.Empty;

    /// <summary>A assinatura secreta do webhook da aplicação, que confere o <c>x-signature</c>.</summary>
    public string SegredoDoWebhook { get; init; } = string.Empty;

    /// <summary>
    /// Quanto esperar cada chamada. Curto de propósito: a tela do formando espera por ela, e o PIX
    /// estático está sempre ali como queda (P7).
    /// </summary>
    public int SegundosDeEspera { get; init; } = 8;

    /// <summary>
    /// O id da conta do próprio Kapa no Mercado Pago — a que recebe os planos (Sprint 37). Nulo enquanto ela
    /// não existe: todo aviso é tratado como de uma turma.
    /// </summary>
    /// <remarks>
    /// Os avisos das turmas (OAuth) e os da conta do Kapa chegam na mesma URL, com o mesmo segredo — o
    /// webhook é da aplicação. O que os separa é o <c>user_id</c> do corpo, que é comparado com este.
    /// </remarks>
    public long? ContaDoKapa { get; init; }

    /// <summary>
    /// O token de acesso da conta do próprio Kapa — é com ele que os planos são cobrados (Sprint 37), sem OAuth.
    /// </summary>
    /// <remarks>
    /// O de produção mora em <c>.env.production</c>; em desenvolvimento, o do vendedor de teste do sandbox, nos
    /// user-secrets. Nunca vai para log.
    /// </remarks>
    public string AccessTokenDoKapa { get; init; } = string.Empty;

    /// <summary>Se a aplicação está configurada — sem ela, nenhuma turma conecta.</summary>
    public bool Ligado => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
