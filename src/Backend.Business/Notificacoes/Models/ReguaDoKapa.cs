namespace Backend.Business.Notificacoes.Models;

/// <summary>Assunto e corpo de uma mensagem, com as variáveis de <c>TemplateDeNotificacao</c>.</summary>
/// <param name="Assunto">Assunto, sem variável: é o que a tela da régua mostra como está.</param>
/// <param name="Corpo">Corpo.</param>
public sealed record TextoDaMensagem(string Assunto, string Corpo);

/// <summary>Um degrau da régua do Kapa: quando dispara e o que diz.</summary>
/// <param name="Gatilho">O que dispara.</param>
/// <param name="DiasDeDeslocamento">Dias de distância do gatilho; negativo é antes do vencimento.</param>
/// <param name="Texto">
/// A mensagem do degrau: ao formando no vencimento, à tesouraria no informe parado.
/// </param>
/// <param name="ResumoDaTesouraria">
/// O resumo que a tesouraria recebe no mesmo dia, quando o degrau a avisa.
/// </param>
public sealed record DegrauDaRegua(GatilhoDaRegua Gatilho, int DiasDeDeslocamento, TextoDaMensagem Texto, TextoDaMensagem? ResumoDaTesouraria = null)
{
    /// <summary>Se a tesouraria recebe um resumo além do aviso ao formando — o D+15 e o D+30.</summary>
    public bool AvisaTesouraria => ResumoDaTesouraria is not null;
}

/// <summary>
/// A régua de cobrança, igual para toda turma: os degraus e o texto de cada um.
/// </summary>
/// <remarks>
/// Decisão de 24/09/2026: o e-mail é padrão do Kapa, e a comissão só liga ou desliga cada degrau.
/// Texto editável pela turma era editor, variável, prévia e validação para uma coisa que quase ninguém
/// muda — e abria espaço para o e-mail com a marca do Kapa dizer o que o Kapa não diria.
/// <para>
/// O banco guarda só o degrau e se ele está ligado; o texto mora aqui, e mudar uma frase é deploy.
/// </para>
/// </remarks>
public static class ReguaDoKapa
{
    /// <summary>Os degraus, na ordem em que o formando os recebe.</summary>
    public static readonly IReadOnlyList<DegrauDaRegua> Degraus =
    [
        new(
            GatilhoDaRegua.Vencimento,
            -5,
            new(
                "Sua parcela vence em 5 dias",
                "Oi, {nome}! Sua parcela de {formatura} vence em {vencimento}, no valor de {valor}. O PIX para pagar está no seu extrato."
            )
        ),
        new(
            GatilhoDaRegua.Vencimento,
            0,
            new(
                "Sua parcela vence hoje",
                "Oi, {nome}! Sua parcela de {formatura} vence hoje, {vencimento}, no valor de {valor}. O PIX para pagar está no seu extrato."
            )
        ),
        new(
            GatilhoDaRegua.Vencimento,
            3,
            new(
                "Parcela em atraso",
                "Oi, {nome}. A parcela de {formatura} que venceu em {vencimento} ainda está em aberto, e o valor "
                    + "atualizado é {valor}. Se você já pagou, avise pelo extrato para a tesouraria conferir."
            )
        ),
        new(
            GatilhoDaRegua.Vencimento,
            15,
            new(
                "Parcela com 15 dias de atraso",
                "Oi, {nome}. A parcela de {formatura} que venceu em {vencimento} está em aberto há 15 dias, e o "
                    + "valor atualizado é {valor}. Pague pelo extrato ou, se já pagou, avise por lá."
            ),
            new(
                "Parcelas com 15 dias de atraso",
                "Hoje {quantidade} parcela(s) de {formatura} completaram 15 dias em aberto, e os formandos já "
                    + "receberam o aviso. Veja no extrato da turma quem está devendo."
            )
        ),
        new(
            GatilhoDaRegua.Vencimento,
            30,
            new(
                "Parcela com 30 dias de atraso",
                "Oi, {nome}. A parcela de {formatura} que venceu em {vencimento} está em aberto há 30 dias, e o "
                    + "valor atualizado é {valor}. A tesouraria da turma foi avisada: procure-a para regularizar."
            ),
            new(
                "Parcelas com 30 dias de atraso",
                "Hoje {quantidade} parcela(s) de {formatura} completaram 30 dias em aberto, e os formandos já "
                    + "receberam o aviso. Veja no extrato da turma quem está devendo."
            )
        ),
        new(
            GatilhoDaRegua.InformePendente,
            3,
            new(
                "Pagamentos esperando conferência",
                "Há {quantidade} aviso(s) de pagamento parados há 3 dias ou mais na fila de {formatura}. "
                    + "Enquanto eles não forem conferidos, a régua não cobra essas parcelas."
            )
        ),
    ];

    /// <summary>O degrau do par <c>(gatilho, dias)</c>, ou nulo se ele não faz parte da régua.</summary>
    /// <remarks>
    /// Nulo é o degrau que uma turma criou quando o texto era editável: ele fica no banco, porque o
    /// histórico de envios aponta para ele, mas a régua não o usa mais.
    /// </remarks>
    /// <param name="gatilho">O que dispara.</param>
    /// <param name="dias">Dias de distância do gatilho.</param>
    public static DegrauDaRegua? De(GatilhoDaRegua gatilho, int dias) =>
        Degraus.FirstOrDefault(d => d.Gatilho == gatilho && d.DiasDeDeslocamento == dias);
}
