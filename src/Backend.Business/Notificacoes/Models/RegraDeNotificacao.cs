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

/// <summary>Por onde a mensagem sai.</summary>
/// <remarks>
/// <see cref="Whatsapp"/> existe no enum e <b>não</b> tem implementação nesta sprint (decisão 6):
/// a régua pula a regra cujo canal não tem canal registrado, em vez de engolir a mensagem.
/// </remarks>
public enum CanalDeNotificacao
{
    /// <summary>E-mail, pela fila que o worker já esvazia.</summary>
    Email,

    /// <summary>API oficial do WhatsApp. Implementação na sprint seguinte.</summary>
    Whatsapp,
}

/// <summary>
/// Um degrau da régua: quando avisar, por onde e com que texto.
/// </summary>
/// <remarks>
/// A turma que não configurar nada recebe <see cref="Padrao"/> (decisão 5): exigir configuração
/// antes de funcionar significa que metade das turmas nunca terá lembrete.
/// <para>
/// O par <c>(Gatilho, DiasDeDeslocamento)</c> tem índice único por formatura — é a identidade do
/// degrau, e é por ele que a gravação da régua encontra o que atualizar.
/// </para>
/// </remarks>
public class RegraDeNotificacao : EntidadeDaFormatura
{
    /// <summary>O que dispara.</summary>
    public GatilhoDaRegua Gatilho { get; private set; }

    /// <summary>Dias de distância do gatilho. Negativo é antes do vencimento.</summary>
    public int DiasDeDeslocamento { get; private set; }

    /// <summary>Por onde sai.</summary>
    public CanalDeNotificacao Canal { get; private set; } = CanalDeNotificacao.Email;

    /// <summary>Assunto da mensagem, com as mesmas variáveis do corpo.</summary>
    public string Assunto { get; private set; } = string.Empty;

    /// <summary>Corpo da mensagem, com as variáveis de <c>TemplateDeNotificacao</c>.</summary>
    public string Template { get; private set; } = string.Empty;

    /// <summary>Se o degrau dispara. Desligado, a régua o pula sem gravar nada.</summary>
    public bool Ativa { get; private set; } = true;

    /// <summary>Manda também à tesouraria — é o "e notificação à tesouraria" do D+30.</summary>
    public bool AvisarTesouraria { get; private set; }

    /// <summary>O tipo da mensagem, para a preferência do titular. Toda régua é cobrança.</summary>
    public static TipoDeNotificacao Tipo => TipoDeNotificacao.Cobranca;

    /// <summary>
    /// A régua padrão, na ordem em que a linha do tempo a desenha.
    /// </summary>
    /// <remarks>
    /// Materializada na primeira leitura da turma (<c>NotificacaoService.ListarRegras</c>), e não na
    /// criação da formatura: as turmas que já existem também precisam dela, e ninguém vai rodar um
    /// script para elas.
    /// </remarks>
    public static IReadOnlyList<RegraDeNotificacao> Padrao() =>
        [
            Nova(
                GatilhoDaRegua.Vencimento,
                -5,
                "Sua parcela de {formatura} vence em breve",
                "Oi, {nome}! Sua parcela de {formatura} vence em {vencimento}, no valor de {valor}. " + "É só abrir o extrato para copiar o PIX."
            ),
            Nova(
                GatilhoDaRegua.Vencimento,
                0,
                "Sua parcela de {formatura} vence hoje",
                "Oi, {nome}! Sua parcela de {formatura} vence hoje, {vencimento}, no valor de {valor}. " + "O PIX está no seu extrato."
            ),
            Nova(
                GatilhoDaRegua.Vencimento,
                3,
                "Parcela em atraso — {formatura}",
                "Oi, {nome}. Consta em aberto a parcela de {formatura} com vencimento em {vencimento}. "
                    + "O valor atualizado é {valor}. Se você já pagou, avise pelo extrato para a tesouraria conferir."
            ),
            Nova(
                GatilhoDaRegua.Vencimento,
                15,
                "Parcela vencida com multa e juros — {formatura}",
                "Oi, {nome}. A parcela de {formatura} com vencimento em {vencimento} segue em aberto e já "
                    + "acumula multa e juros: o valor atualizado é {valor}."
            ),
            Nova(
                GatilhoDaRegua.Vencimento,
                30,
                "Parcela vencida há 30 dias — {formatura}",
                "Oi, {nome}. A parcela de {formatura} com vencimento em {vencimento} está em aberto há 30 dias, "
                    + "e o valor atualizado é {valor}. Procure a tesouraria da turma para regularizar.",
                avisarTesouraria: true
            ),
            Nova(
                GatilhoDaRegua.InformePendente,
                3,
                "{quantidade} pagamentos esperando conferência — {formatura}",
                "Há {quantidade} avisos de pagamento parados há 3 dias ou mais na fila de {formatura}. "
                    + "Enquanto eles não forem conferidos, a régua não cobra essas parcelas."
            ),
        ];

    /// <summary>Um degrau novo.</summary>
    /// <param name="gatilho">O que dispara.</param>
    /// <param name="dias">Dias de distância do gatilho.</param>
    /// <param name="assunto">Assunto da mensagem.</param>
    /// <param name="template">Corpo da mensagem.</param>
    /// <param name="canal">Por onde sai.</param>
    /// <param name="ativa">Se dispara.</param>
    /// <param name="avisarTesouraria">Se a tesouraria recebe cópia.</param>
    public static RegraDeNotificacao Nova(
        GatilhoDaRegua gatilho,
        int dias,
        string assunto,
        string template,
        CanalDeNotificacao canal = CanalDeNotificacao.Email,
        bool ativa = true,
        bool avisarTesouraria = false
    ) =>
        new()
        {
            Gatilho = gatilho,
            DiasDeDeslocamento = dias,
            Assunto = assunto,
            Template = template,
            Canal = canal,
            Ativa = ativa,
            AvisarTesouraria = avisarTesouraria,
        };

    /// <summary>Aplica o que a tesouraria editou. Gatilho e deslocamento são a identidade e não mudam.</summary>
    /// <param name="dados">Texto, canal e chaves do degrau.</param>
    public void Aplicar(DadosDaRegra dados)
    {
        Assunto = dados.Assunto.Trim();
        Template = dados.Template.Trim();
        Canal = dados.Canal;
        Ativa = dados.Ativa;
        AvisarTesouraria = dados.AvisarTesouraria;
    }
}
