namespace Backend.Business.Leads.Settings;

/// <summary>
/// Para onde vai o aviso de contato novo da página institucional.
/// </summary>
public sealed class LeadsSettings
{
    /// <summary>Nome da seção correspondente no arquivo de configuração.</summary>
    public const string Secao = "Leads";

    /// <summary>
    /// Caixa do comercial. Vazio desliga o aviso — o contato continua sendo gravado.
    /// </summary>
    /// <remarks>
    /// Desligar o aviso não pode perder o contato: quem preencheu o formulário fez a parte dele, e
    /// a lista do painel é a fonte da verdade. O e-mail é conveniência.
    /// </remarks>
    public string EmailDoComercial { get; init; } = string.Empty;

    /// <summary>
    /// Janela em que o mesmo e-mail não gera um segundo contato.
    /// </summary>
    /// <remarks>
    /// Um dia. Cobre o duplo clique, o "será que enviou?" e o robô que reenvia o mesmo formulário —
    /// e é curta o bastante para quem voltou uma semana depois, decidido, chegar ao comercial.
    /// </remarks>
    public int HorasParaRepetirOMesmoEmail { get; init; } = 24;
}
