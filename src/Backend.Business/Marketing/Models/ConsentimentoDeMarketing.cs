namespace Backend.Business.Marketing.Models;

/// <summary>
/// Uma linha do histórico de "Receber novidades do Kapa": o aceite ou a oposição, com data e origem.
/// </summary>
/// <remarks>
/// Append-only, como <c>ConsentimentoRegistrado</c> (Sprint 1): sair grava uma linha nova, e o banco
/// recusa <c>UPDATE</c> e <c>DELETE</c>. Tabela própria, e não um <c>DocumentoLegal</c> a mais: o fluxo
/// dos documentos é o do aceite <b>obrigatório</b> — versão vigente pendente manda a pessoa aceitar de
/// novo na entrada —, e esta caixa é opcional e nasce desmarcada (Sprint 40, P1).
/// <para>
/// O estado atual mora em <c>Usuario.ReceberComunicacaoDoKapa</c>, que é o que o worker lê a cada envio;
/// esta tabela é a prova de quando e de onde ele mudou.
/// </para>
/// </remarks>
public class ConsentimentoDeMarketing
{
    /// <summary>Identificador do registro.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Titular.</summary>
    public Guid UsuarioId { get; init; }

    /// <summary>Verdadeiro no aceite, falso na oposição.</summary>
    public bool Aceito { get; init; }

    /// <summary>De onde veio — um de <see cref="OrigemDoConsentimentoDeMarketing"/>.</summary>
    public string Origem { get; init; } = string.Empty;

    /// <summary>Versão do texto da caixa que a pessoa viu (<see cref="TextoDoConsentimentoDeMarketing.Versao"/>).</summary>
    public string VersaoDoTexto { get; init; } = string.Empty;

    /// <summary>Quando, em UTC.</summary>
    public DateTime RegistradoEm { get; init; }

    /// <summary>IP de onde veio; vazio quando o pedido chegou pelo cliente de e-mail sem IP útil.</summary>
    public string EnderecoIp { get; init; } = string.Empty;

    /// <summary>Navegador ou cliente de e-mail que enviou.</summary>
    public string UserAgent { get; init; } = string.Empty;
}

/// <summary>De onde veio a mudança da preferência.</summary>
/// <remarks>Texto estável, gravado no banco e exportado — é dado, não enum.</remarks>
public static class OrigemDoConsentimentoDeMarketing
{
    /// <summary>A caixa do cadastro.</summary>
    public const string Cadastro = "cadastro";

    /// <summary>O link ou o botão de descadastro do próprio e-mail.</summary>
    public const string DescadastroPeloEmail = "descadastro_pelo_email";

    /// <summary>O interruptor de "Minha privacidade".</summary>
    public const string MinhaPrivacidade = "minha_privacidade";
}

/// <summary>O texto da caixa do cadastro e a versão dele, que vai para cada registro.</summary>
/// <remarks>Mudou a frase no front, suba a versão aqui — o registro prova qual texto a pessoa viu.</remarks>
public static class TextoDoConsentimentoDeMarketing
{
    /// <summary>Versão do texto vigente.</summary>
    public const string Versao = "1";

    /// <summary>A frase da caixa, como o front a mostra.</summary>
    public const string Texto = "Quero receber dicas e novidades do Kapa por e-mail.";
}
